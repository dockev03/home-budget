using System.Globalization;
using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Exports;

/// <summary>Export des opérations filtrées en CSV ou en Excel (H5), ouvert à tous les rôles comme la lecture.</summary>
public static partial class ExportEndpoints
{
    public const string CsvContentType = "text/csv; charset=utf-8";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exports").WithTags("Exports");

        group.MapGet("/transactions", ExportTransactionsAsync)
            .Produces(StatusCodes.Status200OK, contentType: CsvContentType, additionalContentTypes: [XlsxContentType])
            .ProducesValidationProblem()
            .WithSummary("Exporte les opérations filtrées (mêmes filtres que la recherche), une ligne par ligne de ventilation. format : csv (défaut) ou xlsx.");

        return app;
    }

    private static async Task<IResult> ExportTransactionsAsync(
        AppDbContext db,
        TimeProvider clock,
        ClaimsPrincipal user,
        ILoggerFactory loggers,
        AuditTrail audit,
        string? format,
        Guid? accountId,
        Guid? categoryId,
        Guid? payeeId,
        DateOnly? from,
        DateOnly? to,
        decimal? minAmount,
        decimal? maxAmount,
        string? search,
        bool? cleared,
        Guid? tagId,
        CancellationToken ct)
    {
        var kind = (format ?? "csv").ToLowerInvariant();
        if (kind is not ("csv" or "xlsx"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Format attendu : csv ou xlsx."] });
        }

        var filter = new TransactionFilter(accountId, categoryId, payeeId, from, to, minAmount, maxAmount, search, cleared, tagId);
        var rows = await LoadRowsAsync(db, filter, ct);

        LogExport(loggers.CreateLogger("HomeBudget.Exports"), user.Identity?.Name, kind, rows.Count);
        await audit.RecordAsync(AuditAction.Exported, "Transaction", details: $"Export {kind.ToUpperInvariant()} : {rows.Count} ligne(s).");

        var stamp = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return kind == "csv"
            ? TypedResults.File(TransactionExportWriter.ToCsv(rows), CsvContentType, $"operations-{stamp}.csv")
            : TypedResults.File(TransactionExportWriter.ToXlsx(rows), XlsxContentType, $"operations-{stamp}.xlsx");
    }

    /// <summary>Lignes de ventilation des opérations filtrées, dans l'ordre chronologique.</summary>
    internal static async Task<List<TransactionExportRow>> LoadRowsAsync(AppDbContext db, TransactionFilter filter, CancellationToken ct)
    {
        var transactions = await filter.ApplyAsync(db, db.Transactions.AsNoTracking(), ct);
        var lines = await transactions
            .SelectMany(t => t.Lines, (t, l) => new
            {
                t.Id,
                t.Date,
                t.AccountId,
                t.PayeeId,
                t.Label,
                t.Notes,
                t.IsCleared,
                t.TransferId,
                Total = t.Amount,
                l.CategoryId,
                l.Amount,
                l.Memo,
                l.SortOrder,
                LineId = l.Id,
            })
            .OrderBy(x => x.Date).ThenBy(x => x.Id).ThenBy(x => x.SortOrder)
            .ToListAsync(ct);

        var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var payees = await db.Payees.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var tags = (await db.TransactionLines.AsNoTracking()
                .SelectMany(l => l.Tags, (l, g) => new { l.Id, g.Name })
                .ToListAsync(ct))
            .ToLookup(x => x.Id, x => x.Name);
        var transfers = (await db.Transactions.AsNoTracking()
                .Where(t => t.TransferId != null)
                .Select(t => new { t.Id, TransferId = t.TransferId!.Value, t.AccountId })
                .ToListAsync(ct))
            .ToLookup(t => t.TransferId);

        return [.. lines.Select(x =>
        {
            var category = x.CategoryId is { } id ? categories.GetValueOrDefault(id) : null;
            var parent = category?.ParentId is { } parentId ? categories.GetValueOrDefault(parentId) : null;
            var counterpart = x.TransferId is { } transferId ? transfers[transferId].FirstOrDefault(t => t.Id != x.Id) : null;
            return new TransactionExportRow(
                x.Date,
                accounts.GetValueOrDefault(x.AccountId) ?? string.Empty,
                x.PayeeId is { } payeeId ? payees.GetValueOrDefault(payeeId) : null,
                x.Label,
                parent?.Name ?? category?.Name,
                parent is null ? null : category?.Name,
                category is null ? null : KindLabel(category.Kind),
                x.Amount,
                x.Memo,
                x.Total,
                x.IsCleared,
                counterpart is null ? null : accounts.GetValueOrDefault(counterpart.AccountId),
                x.Notes,
                x.Id,
                string.Join(", ", tags[x.LineId].Order(StringComparer.CurrentCultureIgnoreCase)));
        })];
    }

    private static string KindLabel(CategoryKind kind) => kind switch
    {
        CategoryKind.Income => "Revenu",
        CategoryKind.Fixed => "Dépense fixe",
        CategoryKind.Variable => "Dépense variable",
        CategoryKind.Savings => "Épargne",
        _ => kind.ToString(),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Export des opérations par {User} : format {Format}, {Count} lignes.")]
    private static partial void LogExport(ILogger logger, string? user, string format, int count);
}
