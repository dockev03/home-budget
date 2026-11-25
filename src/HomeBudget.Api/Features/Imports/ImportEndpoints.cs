using System.Security.Claims;

using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Imports;

/// <summary>
/// Import des relevés bancaires (H1 à H3, E6) : aperçu avec repérage des doublons et des échéances,
/// puis import des opérations choisies. Le fichier est envoyé en Base64 aux deux étapes : rien n'est stocké
/// entre l'aperçu et l'import.
/// </summary>
public static partial class ImportEndpoints
{
    private const int HistorySize = 20;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        group.MapGet("/", ListAsync)
            .WithSummary("Derniers imports, du plus récent au plus ancien.");
        group.MapPost("/preview", PreviewAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithMetadata(new RequestSizeLimitAttribute(ImportLimits.MaxRequestBytes))
            .WithSummary("Lit un relevé (CSV, TSV, OFX ou QIF) sans rien enregistrer : opérations, doublons et échéances correspondantes.");
        group.MapPost("/", CommitAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithMetadata(new RequestSizeLimitAttribute(ImportLimits.MaxRequestBytes))
            .Produces<ImportResultDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Importe les opérations choisies du relevé, en une seule transaction. Les doublons certains sont écartés.");

        return app;
    }

    private static async Task<Ok<List<ImportBatchDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        // Identifiants UUID v7 : l'ordre des identifiants est celui de la création.
        var batches = await db.ImportBatches.AsNoTracking().OrderByDescending(b => b.Id).Take(HistorySize).ToListAsync(ct);
        return TypedResults.Ok(batches.ConvertAll(b =>
            new ImportBatchDto(b.Id, b.AccountId, b.FileName, b.Format, b.Created, b.Matched, b.Duplicates, b.CreatedAt)));
    }

    private static async Task<Results<Ok<ImportPreviewDto>, ValidationProblem>> PreviewAsync(
        ImportFileRequest request, ImportService imports, CancellationToken ct)
    {
        var (analysis, errors) = await imports.AnalyzeAsync(request.AccountId, request.FileName, request.Content, request.ProfileId, ct);
        if (analysis is null)
        {
            return TypedResults.ValidationProblem(errors);
        }
        return TypedResults.Ok(new ImportPreviewDto(
            analysis.Format,
            analysis.Profile?.Name,
            [.. analysis.Lines.Select(l => new ImportPreviewLineDto(
                l.Source.Line.Line,
                l.Source.Line.Date,
                l.Source.Line.Amount,
                l.Source.Line.Label,
                l.Status,
                l.Duplicate?.Id,
                l.Duplicate?.Label,
                l.Duplicate?.Date,
                l.Match is { } m
                    ? new ImportOccurrenceDto(
                        m.Recurrence.Id,
                        RecurrenceSchedule.Label(m.Recurrence.Label, m.Recurrence.IsInstallment, m.Index, m.Recurrence.Count),
                        m.Date,
                        m.Expected,
                        m.Recurrence.CategoryId)
                    : null,
                l.Suggestion?.PayeeName,
                l.Suggestion?.CategoryId))],
            analysis.Errors));
    }

    private static async Task<IResult> CommitAsync(
        ImportCommitRequest request, ImportService imports, ClaimsPrincipal user, ILoggerFactory loggers, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["Aucune opération sélectionnée."] });
        }
        var (analysis, errors) = await imports.AnalyzeAsync(request.AccountId, request.FileName, request.Content, request.ProfileId, ct);
        if (analysis is null)
        {
            return TypedResults.ValidationProblem(errors);
        }
        var choices = request.Lines.GroupBy(l => l.Line).ToDictionary(g => g.Key, g => g.First().AcceptMatch);
        try
        {
            var batch = await imports.CommitAsync(analysis, choices, ct);
            LogImport(loggers.CreateLogger("HomeBudget.Imports"), user.Identity?.Name, batch.Created, batch.Matched, batch.Duplicates, batch.AccountId);
            return TypedResults.Ok(new ImportResultDto(batch.Id, batch.Created, batch.Matched, batch.Duplicates));
        }
        catch (ImportConflictException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Import concurrent.",
                detail: "Ce relevé vient d'être importé ou une échéance a changé : relancez l'aperçu.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relevé importé par {User} : {Created} opérations, dont {Matched} échéances rapprochées, {Duplicates} doublons écartés (compte {AccountId}).")]
    private static partial void LogImport(ILogger logger, string? user, int created, int matched, int duplicates, Guid accountId);
}
