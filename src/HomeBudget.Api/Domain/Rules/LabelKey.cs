using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace HomeBudget.Api.Domain.Rules;

/// <summary>
/// Clé d'un libellé bancaire : les mots significatifs, sans accents, dates, références ni mots de service
/// (« PRLV SEPA MACIF SA ECH/251025 » → « MACIF SA »). Deux opérations d'un même tiers ont la même clé d'un
/// relevé à l'autre.
/// </summary>
public static class LabelKey
{
    /// <summary>Nombre de mots significatifs retenus.</summary>
    public const int MaxWords = 3;

    public const int MaxLength = 100;

    /// <summary>Mots de service des libellés bancaires français, sans valeur pour reconnaître le tiers.</summary>
    private static readonly FrozenSet<string> Noise = FrozenSet.ToFrozenSet(
    [
        "PRLV", "PRELEVEMENT", "PRELEVT", "PRLVT", "SEPA", "CB", "CARTE", "VIR", "VIRT", "VIREMENT", "RECU", "EMIS",
        "INST", "INSTANTANE", "PAIEMENT", "PAIEMT", "PAR", "ACHAT", "FACTURE", "ECH", "ECHEANCE", "REF", "REFERENCE",
        "ID", "EMETTEUR", "MDT", "MANDAT", "LIB", "MOTIF", "DE", "DU", "DES", "LA", "LE", "LES", "ET", "EN", "AU",
        "AUX", "POUR", "FR", "EUR", "WWW", "COM", "HTTP", "HTTPS",
    ], StringComparer.Ordinal);

    /// <summary>Clé du libellé, vide s'il n'a aucun mot significatif.</summary>
    public static string From(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }
        var words = new List<string>(MaxWords);
        foreach (var word in Words(label))
        {
            if (word.Length < 2 || word.Any(char.IsDigit) || Noise.Contains(word))
            {
                continue;
            }
            words.Add(word);
            if (words.Count == MaxWords)
            {
                break;
            }
        }
        var key = string.Join(' ', words);
        return key.Length > MaxLength ? key[..MaxLength].TrimEnd() : key;
    }

    /// <summary>Mots du libellé en majuscules sans accents, coupés sur tout ce qui n'est ni lettre ni chiffre.</summary>
    private static IEnumerable<string> Words(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
            else if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }
        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }
}
