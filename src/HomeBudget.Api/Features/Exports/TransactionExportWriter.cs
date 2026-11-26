using System.Globalization;
using System.Text;

using ClosedXML.Excel;

namespace HomeBudget.Api.Features.Exports;

/// <summary>Une ligne de ventilation exportée, avec les informations de son opération.</summary>
public sealed record TransactionExportRow(
    DateOnly Date,
    string Account,
    string? Payee,
    string? Label,
    string? Category,
    string? Subcategory,
    string? Kind,
    decimal Amount,
    string? Memo,
    decimal TransactionAmount,
    bool IsCleared,
    string? TransferAccount,
    string? Notes,
    Guid TransactionId,
    string Tags = "");

/// <summary>Écriture de l'export des opérations (H5) en CSV « Excel français » ou en classeur .xlsx.</summary>
public static class TransactionExportWriter
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "Date", "Compte", "Tiers", "Libellé", "Catégorie", "Sous-catégorie", "Type", "Montant", "Mémo",
        "Montant de l'opération", "Pointée", "Virement avec", "Notes", "Identifiant de l'opération", "Tags",
    ];

    // Virgule décimale, signe moins ASCII et aucun séparateur de milliers : relu tel quel par Excel en français.
    internal static readonly NumberFormatInfo FrenchNumbers = new() { NumberDecimalSeparator = ",", NegativeSign = "-" };

    /// <summary>CSV pour Excel en français : UTF-8 avec BOM, séparateur « ; », virgule décimale, dates JJ/MM/AAAA.</summary>
    public static byte[] ToCsv(IEnumerable<TransactionExportRow> rows)
    {
        var csv = new StringBuilder();
        AppendLine(csv, Headers);
        foreach (var row in rows)
        {
            AppendLine(csv,
            [
                row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Text(row.Account),
                Text(row.Payee),
                Text(row.Label),
                Text(row.Category),
                Text(row.Subcategory),
                Text(row.Kind),
                row.Amount.ToString("0.00", FrenchNumbers),
                Text(row.Memo),
                row.TransactionAmount.ToString("0.00", FrenchNumbers),
                row.IsCleared ? "Oui" : "Non",
                Text(row.TransferAccount),
                Text(row.Notes),
                row.TransactionId.ToString(),
                Text(row.Tags),
            ]);
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>Classeur d'une feuille : dates et montants typés, en-tête figé et filtres automatiques.</summary>
    public static byte[] ToXlsx(IReadOnlyList<TransactionExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Opérations");
        for (var c = 0; c < Headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = Headers[c];
        }
        sheet.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var r = i + 2;
            sheet.Cell(r, 1).Value = row.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(r, 2).Value = Str(row.Account);
            sheet.Cell(r, 3).Value = Str(row.Payee);
            sheet.Cell(r, 4).Value = Str(row.Label);
            sheet.Cell(r, 5).Value = Str(row.Category);
            sheet.Cell(r, 6).Value = Str(row.Subcategory);
            sheet.Cell(r, 7).Value = Str(row.Kind);
            sheet.Cell(r, 8).Value = (double)row.Amount;
            sheet.Cell(r, 9).Value = Str(row.Memo);
            sheet.Cell(r, 10).Value = (double)row.TransactionAmount;
            sheet.Cell(r, 11).Value = row.IsCleared ? "Oui" : "Non";
            sheet.Cell(r, 12).Value = Str(row.TransferAccount);
            sheet.Cell(r, 13).Value = Str(row.Notes);
            sheet.Cell(r, 14).Value = row.TransactionId.ToString();
            sheet.Cell(r, 15).Value = Str(row.Tags.Length == 0 ? null : row.Tags);
        }

        sheet.Column(1).Style.DateFormat.Format = "dd/mm/yyyy";
        sheet.Column(8).Style.NumberFormat.Format = "#,##0.00 \"€\";-#,##0.00 \"€\"";
        sheet.Column(10).Style.NumberFormat.Format = "#,##0.00 \"€\";-#,##0.00 \"€\"";
        // Largeurs fixes : l'ajustement automatique dépend des polices installées sur le serveur.
        double[] widths = [12, 20, 24, 30, 20, 20, 16, 14, 24, 14, 9, 20, 30, 38, 24];
        for (var c = 0; c < widths.Length; c++)
        {
            sheet.Column(c + 1).Width = widths[c];
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, rows.Count + 1, Headers.Count).SetAutoFilter();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    internal static XLCellValue Str(string? value) => value is null ? Blank.Value : value;

    internal static void AppendLine(StringBuilder csv, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                csv.Append(';');
            }
            csv.Append(Quote(fields[i]));
        }
        csv.Append("\r\n");
    }

    private static string Quote(string value) =>
        value.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    /// <summary>
    /// Texte libre : une apostrophe neutralise un début qu'Excel prendrait pour une formule
    /// (injection de formule, CWE-1236). Les montants, eux, sont écrits par le code.
    /// </summary>
    internal static string Text(string? value) =>
        value is { Length: > 0 } && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value ?? string.Empty;
}
