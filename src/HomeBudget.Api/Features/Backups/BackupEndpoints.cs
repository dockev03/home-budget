using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Backups;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace HomeBudget.Api.Features.Backups;

/// <summary>Sauvegarde et restauration complètes au format JSON (H6), réservées aux administrateurs.</summary>
public static partial class BackupEndpoints
{
    /// <summary>Taille maximale d'un fichier de restauration (64 Mio).</summary>
    public const long MaxRestoreBytes = 64L * 1024 * 1024;

    public const string SnapshotLabel = "avant-restauration";

    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backups").WithTags("Backups").RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/export", ExportAsync)
            .Produces<BackupDocument>(contentType: "application/json")
            .WithSummary("Télécharge la sauvegarde JSON des données du foyer (sans comptes utilisateurs ni secrets).");
        group.MapPost("/restore", RestoreAsync)
            .Accepts<BackupDocument>("application/json")
            .Produces<RestoreResultDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithSummary("Remplace toutes les données du foyer par la sauvegarde envoyée (validée en entier d'abord, instantané de la base juste avant). Les utilisateurs sont conservés.");

        return app;
    }

    private static async Task<IResult> ExportAsync(
        BackupRestorer restorer, IOptions<JsonOptions> json, TimeProvider clock, ClaimsPrincipal user, ILoggerFactory loggers, CancellationToken ct)
    {
        var document = await restorer.ExportAsync(ct);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, json.Value.SerializerOptions);
        LogExport(loggers.CreateLogger("HomeBudget.Backups"), user.Identity?.Name, document.Transactions?.Count ?? 0);
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return TypedResults.File(bytes, "application/json", $"home-budget-sauvegarde-{stamp}.json");
    }

    private static async Task<IResult> RestoreAsync(
        HttpContext http,
        BackupRestorer restorer,
        BackupService backups,
        IOptions<JsonOptions> json,
        ClaimsPrincipal user,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger("HomeBudget.Backups");

        // 1. Lecture bornée : au-delà de la limite, le fichier est refusé sans être lu en entier.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxRestoreBytes + 1;
        }
        if (http.Request.ContentLength > MaxRestoreBytes)
        {
            return TooLarge();
        }
        byte[] content;
        try
        {
            content = await ReadBoundedAsync(http.Request.Body, ct);
        }
        catch (InvalidDataException)
        {
            return TooLarge();
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }

        // 2. Validation complète avant de toucher à la base.
        BackupDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<BackupDocument>(content, json.Value.SerializerOptions);
        }
        catch (JsonException e)
        {
            return Invalid([$"Fichier JSON illisible (ligne {(e.LineNumber ?? 0) + 1})."]);
        }
        if (document is null)
        {
            return Invalid(["Le fichier est vide."]);
        }
        var errors = BackupValidator.Validate(document);
        if (errors.Count > 0)
        {
            LogRejected(logger, user.Identity?.Name, errors.Count);
            return Invalid(errors);
        }

        // 3. Instantané de la base, puis restauration en une seule transaction.
        var snapshot = await backups.SnapshotAsync(SnapshotLabel, ct);
        await restorer.RestoreAsync(document, ct);

        var result = new RestoreResultDto(
            document.Accounts!.Count,
            document.Categories!.Count,
            document.Payees!.Count,
            document.Budgets!.Count,
            document.Transactions!.Count,
            document.Recurrences!.Count,
            document.Occurrences!.Count,
            Path.GetFileName(snapshot));
        LogRestored(logger, user.Identity?.Name, result.Transactions, result.Snapshot);
        return TypedResults.Ok(result);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxRestoreBytes)
            {
                throw new InvalidDataException("Fichier trop volumineux.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static IResult Invalid(IReadOnlyList<string> errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [.. errors] }, title: "Sauvegarde invalide : aucune donnée n'a été modifiée.");

    private static IResult TooLarge() => TypedResults.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Fichier trop volumineux.",
        detail: $"Une sauvegarde fait au plus {MaxRestoreBytes / (1024 * 1024)} Mio.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Sauvegarde JSON exportée par {User} ({Transactions} opérations).")]
    private static partial void LogExport(ILogger logger, string? user, int transactions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restauration refusée pour {User} : fichier invalide ({Errors} erreurs).")]
    private static partial void LogRejected(ILogger logger, string? user, int errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Données restaurées par {User} ({Transactions} opérations). Instantané préalable : {Snapshot}.")]
    private static partial void LogRestored(ILogger logger, string? user, int transactions, string snapshot);
}
