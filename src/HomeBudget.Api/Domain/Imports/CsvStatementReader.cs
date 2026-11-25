using System.Globalization;
using System.Text;

namespace HomeBudget.Api.Domain.Imports;

/// <summary>
/// Lecture d'un relevé CSV ou TSV (H1) : champs entre guillemets (avec guillemets doublés et retours à la ligne),
/// colonnes choisies par <see cref="CsvOptions"/>. Les lignes situées avant la première date lisible sont
/// l'en-tête du relevé : elles sont ignorées sans erreur.
/// </summary>
public static class CsvStatementReader
{
    public static StatementReadResult Read(string text, CsvOptions options)
    {
        var lines = new List<StatementLine>();
        var errors = new List<StatementError>();
        var started = false;
        foreach (var (lineNumber, fields) in Records(text, options.Delimiter))
        {
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }
            var date = Field(fields, options.DateColumn);
            var hasDate = DateOnly.TryParseExact(date, options.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);
            if (!started && !hasDate)
            {
                continue;
            }
            started = true;
            if (!hasDate)
            {
                errors.Add(new(lineNumber, $"Date illisible : « {Excerpt(date)} »."));
                continue;
            }
            if (!TryAmount(fields, options, out var amount, out var error))
            {
                errors.Add(new(lineNumber, error));
                continue;
            }
            lines.Add(new StatementLine(lineNumber, parsed, amount, StatementText.CleanLabel(Field(fields, options.LabelColumn)), null));
        }
        if (!started)
        {
            errors.Add(new(0, "Aucune date lisible : vérifiez le séparateur, la colonne et le format de la date."));
        }
        return new StatementReadResult(ImportFormat.Csv, lines, errors);
    }

    private static bool TryAmount(IReadOnlyList<string> fields, CsvOptions options, out decimal amount, out string error)
    {
        error = string.Empty;
        if (options.AmountColumn is { } column)
        {
            var raw = Field(fields, column);
            if (!StatementText.TryParseAmount(raw, options.DecimalSeparator, out amount))
            {
                error = $"Montant illisible : « {Excerpt(raw)} ».";
                return false;
            }
            if (amount == 0)
            {
                error = "Montant nul.";
                return false;
            }
            return true;
        }

        var debitRaw = Field(fields, options.DebitColumn!.Value);
        var creditRaw = Field(fields, options.CreditColumn!.Value);
        decimal debit = 0, credit = 0;
        var debitOk = string.IsNullOrWhiteSpace(debitRaw) || StatementText.TryParseAmount(debitRaw, options.DecimalSeparator, out debit);
        var creditOk = string.IsNullOrWhiteSpace(creditRaw) || StatementText.TryParseAmount(creditRaw, options.DecimalSeparator, out credit);
        amount = Math.Abs(credit) - Math.Abs(debit);
        if (!debitOk || !creditOk)
        {
            error = $"Montant illisible : « {Excerpt(debitOk ? creditRaw : debitRaw)} ».";
            return false;
        }
        if (amount == 0)
        {
            error = "Montant nul ou absent.";
            return false;
        }
        return true;
    }

    private static string Field(IReadOnlyList<string> fields, int column) =>
        column >= 1 && column <= fields.Count ? fields[column - 1].Trim() : string.Empty;

    private static string Excerpt(string value) => value.Length > 40 ? value[..40] + "…" : value;

    /// <summary>Enregistrements du fichier, avec le numéro de leur première ligne.</summary>
    private static IEnumerable<(int Line, List<string> Fields)> Records(string text, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }
                    field.Append(c);
                }
                continue;
            }
            if (c == '"' && field.ToString().Trim().Length == 0)
            {
                field.Clear();
                quoted = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                fields.Add(field.ToString());
                field.Clear();
                yield return (recordLine, fields);
                fields = [];
                line++;
                recordLine = line;
            }
            else
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (recordLine, fields);
        }
    }
}
