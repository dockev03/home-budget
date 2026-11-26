using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Reports;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Reports;

/// <summary>Rapports personnalisés enregistrés (G8), communs au foyer.</summary>
public static class SavedReportEndpoints
{
    public static IEndpointRouteBuilder MapSavedReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports/saved").WithTags("Reports");

        group.MapGet("/", ListAsync)
            .Produces<List<SavedReportDto>>()
            .WithSummary("Rapports enregistrés, par nom.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<SavedReportDto>(StatusCodes.Status201Created).ProducesValidationProblem()
            .WithSummary("Enregistre un rapport personnalisé.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<SavedReportDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un rapport enregistré (If-Match requis).");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un rapport enregistré (If-Match requis).");

        return app;
    }

    public sealed record SavedReportRequest(
        [property: Required, StringLength(SavedReport.MaxNameLength, MinimumLength = 1)] string Name,
        ReportGroupBy GroupBy,
        ReportRange Range,
        DateOnly? From,
        DateOnly? To,
        List<Guid>? AccountIds,
        List<Guid>? CategoryIds,
        List<Guid>? PayeeIds,
        List<Guid>? TagIds,
        List<CategoryKind>? Kinds,
        bool IncludeTransfers = false);

    public sealed record SavedReportDto(
        Guid Id,
        string Name,
        ReportGroupBy GroupBy,
        ReportRange Range,
        DateOnly? From,
        DateOnly? To,
        List<Guid> AccountIds,
        List<Guid> CategoryIds,
        List<Guid> PayeeIds,
        List<Guid> TagIds,
        List<CategoryKind> Kinds,
        bool IncludeTransfers,
        int Version);

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.SavedReports.AsNoTracking().ToListAsync(ct))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToDto)
            .ToList());

    private static async Task<IResult> CreateAsync(SavedReportRequest request, AppDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }
        var report = new SavedReport { Name = request.Name.Trim() };
        Apply(report, request);
        db.SavedReports.Add(report);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/reports/saved/{report.Id}", ToDto(report));
    }

    private static async Task<IResult> UpdateAsync(Guid id, SavedReportRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var report = await db.SavedReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, report) is { } precondition)
        {
            return precondition;
        }
        if (Validate(request) is { } problem)
        {
            return problem;
        }
        Apply(report, request);
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(ToDto(report)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var report = await db.SavedReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, report) is { } precondition)
        {
            return precondition;
        }
        db.SavedReports.Remove(report);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static IResult? Validate(SavedReportRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Le nom est obligatoire."];
        }
        if (!Enum.IsDefined(request.GroupBy))
        {
            errors["groupBy"] = ["Regroupement inconnu."];
        }
        if (!Enum.IsDefined(request.Range))
        {
            errors["range"] = ["Plage inconnue."];
        }
        else if (request.Range == ReportRange.Custom
            && (request.From is not { } from || request.To is not { } to || to < from || to.DayNumber - from.DayNumber > CustomReportEndpoints.MaxDays))
        {
            errors["from"] = ["Indiquez une date de début et une date de fin, dans l'ordre (20 ans au plus)."];
        }
        if (new[] { request.AccountIds?.Count, request.CategoryIds?.Count, request.PayeeIds?.Count, request.TagIds?.Count, request.Kinds?.Count }
            .Any(c => c > SavedReport.MaxFilterItems))
        {
            errors["filters"] = [$"{SavedReport.MaxFilterItems} éléments au plus par filtre."];
        }
        if ((request.Kinds ?? []).Any(k => !Enum.IsDefined(k)))
        {
            errors["kinds"] = ["Type de catégorie inconnu."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void Apply(SavedReport report, SavedReportRequest request)
    {
        report.Name = request.Name.Trim();
        report.GroupBy = request.GroupBy;
        report.Range = request.Range;
        report.From = request.Range == ReportRange.Custom ? request.From : null;
        report.To = request.Range == ReportRange.Custom ? request.To : null;
        report.AccountIds = [.. (request.AccountIds ?? []).Distinct()];
        report.CategoryIds = [.. (request.CategoryIds ?? []).Distinct()];
        report.PayeeIds = [.. (request.PayeeIds ?? []).Distinct()];
        report.TagIds = [.. (request.TagIds ?? []).Distinct()];
        report.Kinds = [.. (request.Kinds ?? []).Distinct()];
        report.IncludeTransfers = request.IncludeTransfers;
    }

    private static SavedReportDto ToDto(SavedReport r) => new(
        r.Id, r.Name, r.GroupBy, r.Range, r.From, r.To, r.AccountIds, r.CategoryIds, r.PayeeIds, r.TagIds, r.Kinds, r.IncludeTransfers, r.Version);
}
