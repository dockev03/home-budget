using System.Globalization;
using System.Text;

using ClosedXML.Excel;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Reports;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Exports;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Reports;

/// <summary>
/// Rapports personnalisés (G8) : lignes d'opérations filtrées (période, comptes, catégories, tiers, tags, types),
/// regroupées par catégorie, sous-catégorie, tiers, tag, période ou compte, et exportables. Les lignes de virement sans
/// catégorie en sont exclues par défaut (R5) ; les comptes privés des autres membres n'y figurent jamais (R8).
/// </summary>
public static class CustomReportEndpoints
{
    /// <summary>Nombre maximal de périodes d'une plage saisie (20 ans).</summary>
    public const int MaxDays = 366 * 20;

    public static IEndpointRouteBuilder MapCustomReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports/custom").WithTags("Reports");

        group.MapGet("/", RunAsync)
            .Produces<ReportDto>().ProducesValidationProblem()
            .WithSummary("Exécute un rapport personnalisé : filtres, regroupement et plage (dates saisies, ou périodes relatives à aujourd'hui).");
        group.MapGet("/export", ExportAsync)
            .Produces(StatusCodes.Status200OK, contentType: ExportEndpoints.CsvContentType, additionalContentTypes: [ExportEndpoints.XlsxContentType])
            .ProducesValidationProblem()
            .WithSummary("Exporte le rapport personnalisé en CSV (défaut) ou en Excel (format=xlsx).");

        return app;
    }

    /// <param name="Range">Plage : Custom (défaut) avec From et To, ou périodes relatives à aujourd'hui.</param>
    /// <param name="CategoryIds">Une catégorie principale inclut ses sous-catégories.</param>
    /// <param name="Kinds">Types de catégorie ; sans filtre, les lignes sans catégorie sont comprises.</param>
    /// <param name="IncludeTransfers">Inclure les lignes de virement sans catégorie (exclues par défaut, R5).</param>
    public sealed record ReportQuery(
        [property: FromQuery(Name = "range")] ReportRange? Range,
        [property: FromQuery(Name = "from")] DateOnly? From,
        [property: FromQuery(Name = "to")] DateOnly? To,
        [property: FromQuery(Name = "groupBy")] ReportGroupBy? GroupBy,
        [property: FromQuery(Name = "accountIds")] Guid[]? AccountIds,
        [property: FromQuery(Name = "categoryIds")] Guid[]? CategoryIds,
        [property: FromQuery(Name = "payeeIds")] Guid[]? PayeeIds,
        [property: FromQuery(Name = "tagIds")] Guid[]? TagIds,
        [property: FromQuery(Name = "kinds")] CategoryKind[]? Kinds,
        [property: FromQuery(Name = "includeTransfers")] bool? IncludeTransfers);

    /// <param name="Key">Identifiant du groupe (catégorie, tiers, tag, compte), clé AAAA-MM de la période, ou « none ».</param>
    /// <param name="Credit">Entrées (lignes positives).</param>
    /// <param name="Debit">Sorties (lignes négatives), en positif.</param>
    /// <param name="Net">Somme signée.</param>
    /// <param name="Count">Nombre d'opérations.</param>
    public sealed record ReportRowDto(string Key, string Label, string? Color, CategoryKind? Kind, decimal Credit, decimal Debit, decimal Net, int Count);

    public sealed record ReportDto(
        DateOnly From, DateOnly To, ReportGroupBy GroupBy, List<ReportRowDto> Rows, decimal Credit, decimal Debit, decimal Net, int Count);

    private static async Task<IResult> RunAsync([AsParameters] ReportQuery query, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (report, problem) = await ComputeAsync(query, db, clock, ct);
        return problem ?? TypedResults.Ok(report);
    }

    private static async Task<IResult> ExportAsync(
        [AsParameters] ReportQuery query, string? format, AppDbContext db, TimeProvider clock, AuditTrail audit, CancellationToken ct)
    {
        var kind = (format ?? "csv").ToLowerInvariant();
        if (kind is not ("csv" or "xlsx"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Format attendu : csv ou xlsx."] });
        }
        var (report, problem) = await ComputeAsync(query, db, clock, ct);
        if (problem is not null)
        {
            return problem;
        }
        await audit.RecordAsync(AuditAction.Exported, "Report", details: $"Rapport {kind.ToUpperInvariant()} : {report!.Rows.Count} ligne(s).");
        var stamp = Balances.Today(clock).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return kind == "csv"
            ? TypedResults.File(ToCsv(report), ExportEndpoints.CsvContentType, $"rapport-{stamp}.csv")
            : TypedResults.File(ToXlsx(report), ExportEndpoints.XlsxContentType, $"rapport-{stamp}.xlsx");
    }

    /// <summary>Dates d'une plage relative, selon les périodes budgétaires du foyer (R1).</summary>
    public static (DateOnly From, DateOnly To) Resolve(ReportRange range, PeriodCalculator calculator, DateOnly today)
    {
        var current = calculator.For(today);
        Period Back(int count)
        {
            var p = current;
            for (var i = 0; i < count; i++)
            {
                p = calculator.Previous(p);
            }
            return p;
        }
        (DateOnly, DateOnly) Year(int year) =>
            calculator.TryParse($"{year:D4}-01", out var first) && calculator.TryParse($"{year:D4}-12", out var last)
                ? (first.Start, last.End)
                : (current.Start, current.End);
        return range switch
        {
            ReportRange.PreviousPeriod => (Back(1).Start, Back(1).End),
            ReportRange.Last3Periods => (Back(2).Start, current.End),
            ReportRange.Last12Periods => (Back(11).Start, current.End),
            ReportRange.CurrentYear => Year(current.Year),
            ReportRange.PreviousYear => Year(current.Year - 1),
            _ => (current.Start, current.End),
        };
    }

    internal static async Task<(ReportDto? Report, IResult? Problem)> ComputeAsync(
        ReportQuery query, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var range = query.Range ?? ReportRange.Custom;
        var groupBy = query.GroupBy ?? ReportGroupBy.Category;
        if (!Enum.IsDefined(range))
        {
            errors["range"] = ["Plage inconnue."];
        }
        if (!Enum.IsDefined(groupBy))
        {
            errors["groupBy"] = ["Regroupement inconnu."];
        }
        if ((query.Kinds ?? []).Any(k => !Enum.IsDefined(k)))
        {
            errors["kinds"] = ["Type de catégorie inconnu."];
        }
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        DateOnly from, to;
        if (range == ReportRange.Custom)
        {
            if (query.From is not { } f || query.To is not { } t || t < f)
            {
                errors["from"] = ["Indiquez une date de début et une date de fin, dans l'ordre."];
                from = to = default;
            }
            else if (t.DayNumber - f.DayNumber > MaxDays)
            {
                errors["to"] = ["La plage dépasse 20 ans."];
                from = to = default;
            }
            else
            {
                (from, to) = (f, t);
            }
        }
        else
        {
            (from, to) = Resolve(range, calculator, Balances.Today(clock));
        }
        if (errors.Count > 0)
        {
            return (null, TypedResults.ValidationProblem(errors));
        }

        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var lines = db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new
            {
                Line = l,
                t.Date,
                t.AccountId,
                t.PayeeId,
                IsTransfer = t.TransferId != null,
            })
            .Where(x => x.Date >= from && x.Date <= to);
        if (query.IncludeTransfers != true)
        {
            lines = lines.Where(x => !(x.IsTransfer && x.Line.CategoryId == null));
        }
        if (query.AccountIds is { Length: > 0 } accountIds)
        {
            lines = lines.Where(x => accountIds.Contains(x.AccountId));
        }
        if (query.PayeeIds is { Length: > 0 } payeeIds)
        {
            lines = lines.Where(x => x.PayeeId != null && payeeIds.Contains(x.PayeeId.Value));
        }
        if (query.TagIds is { Length: > 0 } tagIds)
        {
            lines = lines.Where(x => x.Line.Tags.Any(g => tagIds.Contains(g.Id)));
        }
        if (query.CategoryIds is { Length: > 0 } categoryIds)
        {
            var ids = categories.Values.Where(c => categoryIds.Contains(c.Id) || (c.ParentId is { } p && categoryIds.Contains(p)))
                .Select(c => c.Id).ToList();
            lines = lines.Where(x => x.Line.CategoryId != null && ids.Contains(x.Line.CategoryId.Value));
        }
        if (query.Kinds is { Length: > 0 } kinds)
        {
            var ids = categories.Values.Where(c => kinds.Contains(c.Kind)).Select(c => c.Id).ToList();
            lines = lines.Where(x => x.Line.CategoryId != null && ids.Contains(x.Line.CategoryId.Value));
        }
        var rows = await lines
            .Select(x => new { x.Line.Id, x.Line.TransactionId, x.Line.CategoryId, x.Line.Amount, x.Date, x.AccountId, x.PayeeId })
            .ToListAsync(ct);

        var lineIds = rows.Select(r => r.Id).ToHashSet();
        var tagsByLine = groupBy == ReportGroupBy.Tag
            ? (await db.TransactionLines.AsNoTracking()
                    .SelectMany(l => l.Tags, (l, g) => new { l.Id, TagId = g.Id, g.Name })
                    .ToListAsync(ct))
                .Where(x => lineIds.Contains(x.Id))
                .ToLookup(x => x.Id, x => (x.TagId, x.Name))
            : null;
        var payees = groupBy == ReportGroupBy.Payee
            ? await db.Payees.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct)
            : [];
        var accounts = groupBy == ReportGroupBy.Account
            ? await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => (a.Name, a.Color), ct)
            : [];

        const string none = "none";
        IEnumerable<(string Key, string Label, string? Color, CategoryKind? Kind)> Keys(Guid lineId, Guid? categoryId, DateOnly date, Guid accountId, Guid? payeeId)
        {
            var category = categoryId is { } id ? categories.GetValueOrDefault(id) : null;
            var parent = category?.ParentId is { } parentId ? categories.GetValueOrDefault(parentId) : null;
            switch (groupBy)
            {
                case ReportGroupBy.Category:
                    var main = parent ?? category;
                    yield return main is null ? (none, "Sans catégorie", null, null) : (main.Id.ToString(), main.Name, main.Color, main.Kind);
                    break;
                case ReportGroupBy.Subcategory:
                    yield return category is null
                        ? (none, "Sans catégorie", null, null)
                        : (category.Id.ToString(), parent is null ? category.Name : $"{parent.Name} › {category.Name}", category.Color ?? parent?.Color, category.Kind);
                    break;
                case ReportGroupBy.Payee:
                    yield return payeeId is { } p && payees.TryGetValue(p, out var name) ? (p.ToString(), name, null, null) : (none, "Sans tiers", null, null);
                    break;
                case ReportGroupBy.Tag:
                    var any = false;
                    foreach (var (tagId, tagName) in tagsByLine![lineId])
                    {
                        any = true;
                        yield return (tagId.ToString(), tagName, null, null);
                    }
                    if (!any)
                    {
                        yield return (none, "Sans tag", null, null);
                    }
                    break;
                case ReportGroupBy.Period:
                    var key = calculator.For(date).Key;
                    yield return (key, key, null, null);
                    break;
                default:
                    var (accountName, color) = accounts.GetValueOrDefault(accountId, ("?", null));
                    yield return (accountId.ToString(), accountName, color, null);
                    break;
            }
        }

        var groups = new Dictionary<string, Group>();
        if (groupBy == ReportGroupBy.Period)
        {
            for (var p = calculator.For(from); p.Start <= to; p = calculator.Next(p))
            {
                groups[p.Key] = new Group(p.Key, null, null);
            }
        }
        foreach (var row in rows)
        {
            foreach (var (key, label, color, kind) in Keys(row.Id, row.CategoryId, row.Date, row.AccountId, row.PayeeId))
            {
                if (!groups.TryGetValue(key, out var g))
                {
                    groups[key] = g = new Group(label, color, kind);
                }
                g.Transactions.Add(row.TransactionId);
                if (row.Amount >= 0)
                {
                    g.Credit += row.Amount;
                }
                else
                {
                    g.Debit -= row.Amount;
                }
            }
        }

        var result = groups
            .Select(g => new ReportRowDto(g.Key, g.Value.Label, g.Value.Color, g.Value.Kind, g.Value.Credit, g.Value.Debit,
                g.Value.Credit - g.Value.Debit, g.Value.Transactions.Count));
        result = groupBy == ReportGroupBy.Period
            ? result.OrderBy(r => r.Key, StringComparer.Ordinal)
            : result.OrderByDescending(r => Math.Abs(r.Net)).ThenBy(r => r.Label, StringComparer.CurrentCulture);
        var credit = rows.Where(r => r.Amount > 0).Sum(r => r.Amount);
        var debit = -rows.Where(r => r.Amount < 0).Sum(r => r.Amount);
        return (new ReportDto(from, to, groupBy, [.. result], credit, debit, credit - debit, rows.Select(r => r.TransactionId).Distinct().Count()), null);
    }

    private sealed class Group(string label, string? color, CategoryKind? kind)
    {
        public string Label { get; } = label;

        public string? Color { get; } = color;

        public CategoryKind? Kind { get; } = kind;

        public decimal Credit { get; set; }

        public decimal Debit { get; set; }

        public HashSet<Guid> Transactions { get; } = [];
    }

    private static readonly string[] Headers = ["Groupe", "Entrées", "Sorties", "Solde", "Opérations"];

    internal static byte[] ToCsv(ReportDto report)
    {
        var csv = new StringBuilder();
        var numbers = TransactionExportWriter.FrenchNumbers;
        TransactionExportWriter.AppendLine(csv, Headers);
        foreach (var r in report.Rows)
        {
            TransactionExportWriter.AppendLine(csv,
            [
                TransactionExportWriter.Text(r.Label),
                r.Credit.ToString("0.00", numbers),
                r.Debit.ToString("0.00", numbers),
                r.Net.ToString("0.00", numbers),
                r.Count.ToString(CultureInfo.InvariantCulture),
            ]);
        }
        TransactionExportWriter.AppendLine(csv,
        [
            "Total",
            report.Credit.ToString("0.00", numbers),
            report.Debit.ToString("0.00", numbers),
            report.Net.ToString("0.00", numbers),
            report.Count.ToString(CultureInfo.InvariantCulture),
        ]);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    internal static byte[] ToXlsx(ReportDto report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Rapport");
        for (var c = 0; c < Headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Headers[c];
        }
        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in report.Rows)
        {
            sheet.Cell(r, 1).Value = row.Label;
            sheet.Cell(r, 2).Value = (double)row.Credit;
            sheet.Cell(r, 3).Value = (double)row.Debit;
            sheet.Cell(r, 4).Value = (double)row.Net;
            sheet.Cell(r, 5).Value = row.Count;
            r++;
        }
        sheet.Cell(r, 1).Value = "Total";
        sheet.Cell(r, 2).Value = (double)report.Credit;
        sheet.Cell(r, 3).Value = (double)report.Debit;
        sheet.Cell(r, 4).Value = (double)report.Net;
        sheet.Cell(r, 5).Value = report.Count;
        sheet.Row(r).Style.Font.Bold = true;
        for (var c = 2; c <= 4; c++)
        {
            sheet.Column(c).Style.NumberFormat.Format = "#,##0.00 \"€\";-#,##0.00 \"€\"";
            sheet.Column(c).Width = 16;
        }
        sheet.Column(1).Width = 36;
        sheet.Column(5).Width = 12;
        sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
