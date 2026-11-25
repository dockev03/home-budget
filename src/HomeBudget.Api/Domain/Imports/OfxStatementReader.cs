using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace HomeBudget.Api.Domain.Imports;

/// <summary>
/// Lecture d'un relevé OFX (H2), en SGML (OFX 1.x, balises de valeur non fermées) comme en XML (OFX 2.x).
/// Seules les opérations (<c>STMTTRN</c>) sont lues ; le FITID sert d'identifiant pour écarter les doublons.
/// </summary>
public static partial class OfxStatementReader
{
    public static StatementReadResult Read(string text)
    {
        var lines = new List<StatementLine>();
        var errors = new List<StatementError>();
        Dictionary<string, string>? current = null;
        var rank = 0;

        foreach (Match tag in Tags().Matches(text))
        {
            var closing = tag.Groups["close"].Success;
            var name = tag.Groups["name"].Value.ToUpperInvariant();
            if (name == "STMTTRN")
            {
                if (current is not null)
                {
                    Add(current, ++rank, lines, errors);
                }
                current = closing ? null : new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }
            if (current is null)
            {
                continue;
            }
            if (closing && name == "BANKTRANLIST")
            {
                Add(current, ++rank, lines, errors);
                current = null;
                continue;
            }
            var value = WebUtility.HtmlDecode(tag.Groups["value"].Value).Trim();
            if (!closing && value.Length > 0)
            {
                current[name] = value;
            }
        }
        if (current is not null)
        {
            Add(current, ++rank, lines, errors);
        }
        if (rank == 0)
        {
            errors.Add(new(0, "Aucune opération trouvée dans ce fichier OFX."));
        }
        return new StatementReadResult(ImportFormat.Ofx, lines, errors);
    }

    private static void Add(Dictionary<string, string> fields, int rank, List<StatementLine> lines, List<StatementError> errors)
    {
        var posted = fields.GetValueOrDefault("DTPOSTED") ?? string.Empty;
        if (posted.Length < 8
            || !DateOnly.TryParseExact(posted[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add(new(rank, $"Opération {rank} : date illisible."));
            return;
        }
        if (!StatementText.TryParseFlexibleAmount(fields.GetValueOrDefault("TRNAMT"), out var amount) || amount == 0)
        {
            errors.Add(new(rank, $"Opération {rank} : montant illisible ou nul."));
            return;
        }
        var name = fields.GetValueOrDefault("NAME") ?? string.Empty;
        var memo = fields.GetValueOrDefault("MEMO") ?? string.Empty;
        var label = memo.Length > 0 && !name.Contains(memo, StringComparison.OrdinalIgnoreCase) ? $"{name} {memo}" : name;
        if (string.IsNullOrWhiteSpace(label) && fields.TryGetValue("CHECKNUM", out var check))
        {
            label = $"CHEQUE N° {check}";
        }
        var fitId = fields.GetValueOrDefault("FITID");
        lines.Add(new StatementLine(rank, date, amount, StatementText.CleanLabel(label), string.IsNullOrWhiteSpace(fitId) ? null : fitId));
    }

    /// <summary>Balise ouvrante ou fermante, suivie du texte jusqu'à la balise suivante.</summary>
    [GeneratedRegex(@"<(?<close>/)?(?<name>[A-Za-z0-9.]+)>(?<value>[^<]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();
}
