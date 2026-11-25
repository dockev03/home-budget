using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Api.Domain.Imports;

/// <summary>Opération lue, avec son empreinte d'import (H3).</summary>
public sealed record FingerprintedLine(StatementLine Line, string Hash);

/// <summary>Point d'entrée de la lecture d'un relevé, quel que soit son format.</summary>
public static class StatementReader
{
    /// <summary>Nombre maximal d'opérations lues dans un fichier.</summary>
    public const int MaxLines = 10_000;

    /// <summary>
    /// Lit le relevé. Un CSV ou un TSV exige des réglages (<paramref name="csv"/>) ; l'OFX et le QIF se lisent seuls.
    /// </summary>
    public static StatementReadResult Read(ImportFormat format, string text, CsvOptions? csv) => format switch
    {
        ImportFormat.Ofx => OfxStatementReader.Read(text),
        ImportFormat.Qif => QifStatementReader.Read(text),
        ImportFormat.Csv when csv is not null => CsvStatementReader.Read(text, csv),
        _ => throw new ArgumentException("Réglages CSV requis.", nameof(csv)),
    };

    /// <summary>
    /// Empreinte de chaque opération : le FITID de la banque s'il existe ; sinon un condensé de la date, du montant,
    /// du libellé normalisé et du rang parmi les opérations identiques du fichier. Deux achats identiques le même
    /// jour restent ainsi deux opérations, et réimporter le même relevé ne crée aucun doublon.
    /// </summary>
    public static IReadOnlyList<FingerprintedLine> Fingerprint(IEnumerable<StatementLine> lines)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<FingerprintedLine>();
        foreach (var line in lines)
        {
            if (line.ExternalId is { } externalId)
            {
                result.Add(new(line, "fitid:" + externalId));
                continue;
            }
            var key = string.Create(CultureInfo.InvariantCulture, $"{line.Date:yyyy-MM-dd}|{line.Amount:0.00}|{NormalizeKey(line.Label)}");
            var rank = seen.GetValueOrDefault(key);
            seen[key] = rank + 1;
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{rank}"));
            result.Add(new(line, "sha256:" + Convert.ToHexStringLower(digest)));
        }
        return result;
    }

    /// <summary>Libellé comparable : majuscules, sans accents ni ponctuation, espaces réduits.</summary>
    public static string NormalizeKey(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }
        return builder.ToString().TrimEnd();
    }
}
