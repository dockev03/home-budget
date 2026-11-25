using System.Globalization;
using System.Text;

namespace HomeBudget.Api.Domain.Imports;

/// <summary>Décodage des fichiers de relevé et lecture des valeurs communes aux formats (montants, libellés).</summary>
public static class StatementText
{
    /// <summary>Longueur maximale d'un libellé brut conservé.</summary>
    public const int MaxLabelLength = 500;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding Windows1252 = CreateWindows1252();

    /// <summary>
    /// Décode le fichier : d'après sa marque d'ordre des octets (BOM) si elle existe, en UTF-8 s'il est valide,
    /// et sinon en Windows-1252, l'encodage des exports des banques françaises (« Numéro », « Libellé »).
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(content[3..]);
        }
        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(content[2..]);
        }
        if (content.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(content[2..]);
        }
        try
        {
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252.GetString(content);
        }
    }

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    /// <summary>Format du fichier, d'après son contenu (et à défaut son extension).</summary>
    public static ImportFormat DetectFormat(string fileName, string text)
    {
        var head = text.AsSpan(0, Math.Min(text.Length, 2048)).TrimStart();
        if (head.StartsWith("OFXHEADER", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<OFX>", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<?OFX", StringComparison.OrdinalIgnoreCase))
        {
            return ImportFormat.Ofx;
        }
        if (head.StartsWith("!Type:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("!Account", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("!Option", StringComparison.OrdinalIgnoreCase))
        {
            return ImportFormat.Qif;
        }
        return Path.GetExtension(fileName).ToUpperInvariant() switch
        {
            ".OFX" => ImportFormat.Ofx,
            ".QIF" => ImportFormat.Qif,
            _ => ImportFormat.Csv,
        };
    }

    /// <summary>Libellé nettoyé : espaces multiples réduits, caractères de contrôle retirés, longueur bornée.</summary>
    public static string CleanLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space)
            {
                builder.Append(' ');
                space = false;
            }
            builder.Append(c);
        }
        return builder.Length > MaxLabelLength ? builder.ToString(0, MaxLabelLength).TrimEnd() : builder.ToString();
    }

    /// <summary>
    /// Montant écrit avec le séparateur décimal indiqué : « -1 234,56 », « 1.234,56 » ou « -1234.56 ».
    /// Refuse plus de 2 décimales.
    /// </summary>
    public static bool TryParseAmount(string? value, char decimalSeparator, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var thousands = decimalSeparator == ',' ? '.' : ',';
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (c == decimalSeparator)
            {
                builder.Append('.');
            }
            else if (c == thousands || c == '\'' || char.IsWhiteSpace(c) || c == '€')
            {
                continue;
            }
            else if (c == '−')
            {
                builder.Append('-'); // signe moins typographique
            }
            else
            {
                builder.Append(c);
            }
        }
        return decimal.TryParse(builder.ToString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && decimal.Round(amount, 2) == amount
            && Math.Abs(amount) < 1_000_000_000m;
    }

    /// <summary>
    /// Montant dont le séparateur décimal n'est pas connu (OFX, QIF) : le dernier point ou la dernière virgule
    /// suivi d'au plus 2 chiffres est le séparateur décimal.
    /// </summary>
    public static bool TryParseFlexibleAmount(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var trimmed = value.Trim();
        var last = trimmed.LastIndexOfAny([',', '.']);
        var separator = last >= 0 && trimmed.Length - last - 1 <= 2 ? trimmed[last] : '.';
        return TryParseAmount(trimmed, separator, out amount);
    }
}
