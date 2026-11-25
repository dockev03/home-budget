using System.Globalization;

namespace HomeBudget.Api.Domain.Imports;

/// <summary>
/// Lecture d'un relevé QIF (H2) : une opération par bloc terminé par « ^ », dates au format français
/// (jour en premier). Les catégories et les ventilations du fichier ne sont pas reprises.
/// </summary>
public static class QifStatementReader
{
    private static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy-MM-dd"];

    public static StatementReadResult Read(string text)
    {
        var lines = new List<StatementLine>();
        var errors = new List<StatementError>();
        var fields = new Dictionary<char, string>();
        var start = 0;
        var number = 0;

        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '!')
            {
                continue;
            }
            if (fields.Count == 0)
            {
                start = number;
            }
            if (line[0] == '^')
            {
                if (fields.Count > 0)
                {
                    Add(fields, start, lines, errors);
                    fields.Clear();
                }
                continue;
            }
            // Les lignes de ventilation (S, E, $) répètent des clés : seule la première valeur est gardée.
            fields.TryAdd(char.ToUpperInvariant(line[0]), line[1..].Trim());
        }
        if (fields.Count > 0)
        {
            Add(fields, start, lines, errors);
        }
        if (lines.Count == 0 && errors.Count == 0)
        {
            errors.Add(new(0, "Aucune opération trouvée dans ce fichier QIF."));
        }
        return new StatementReadResult(ImportFormat.Qif, lines, errors);
    }

    private static void Add(Dictionary<char, string> fields, int line, List<StatementLine> lines, List<StatementError> errors)
    {
        // Variante « 25/11'26 » de certains logiciels.
        var rawDate = (fields.GetValueOrDefault('D') ?? string.Empty).Replace('\'', '/').Replace(' ', '0');
        if (!DateOnly.TryParseExact(rawDate, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add(new(line, $"Date illisible : « {fields.GetValueOrDefault('D')} »."));
            return;
        }
        var rawAmount = fields.GetValueOrDefault('T') ?? fields.GetValueOrDefault('U');
        if (!StatementText.TryParseFlexibleAmount(rawAmount, out var amount) || amount == 0)
        {
            errors.Add(new(line, $"Montant illisible ou nul : « {rawAmount} »."));
            return;
        }
        var payee = fields.GetValueOrDefault('P') ?? string.Empty;
        var memo = fields.GetValueOrDefault('M') ?? string.Empty;
        var label = memo.Length > 0 && !payee.Contains(memo, StringComparison.OrdinalIgnoreCase) ? $"{payee} {memo}" : payee;
        if (string.IsNullOrWhiteSpace(label) && fields.TryGetValue('N', out var check))
        {
            label = $"CHEQUE N° {check}";
        }
        lines.Add(new StatementLine(line, date, amount, StatementText.CleanLabel(label), null));
    }
}
