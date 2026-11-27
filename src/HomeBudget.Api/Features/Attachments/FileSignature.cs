using System.Text;

namespace HomeBudget.Api.Features.Attachments;

/// <summary>
/// Reconnaissance du type d'un fichier sur ses premiers octets (B7) : seuls JPEG, PNG, WebP, HEIC et PDF sont acceptés,
/// quels que soient l'extension et le type annoncés par le client.
/// </summary>
public static class FileSignature
{
    /// <summary>Nombre d'octets lus pour reconnaître le type.</summary>
    public const int HeaderLength = 16;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";
    public const string Heic = "image/heic";
    public const string Pdf = "application/pdf";

    // Marques ISO-BMFF des images HEIF/HEIC (photos d'iPhone).
    private static readonly string[] HeifBrands = ["heic", "heix", "heim", "heis", "hevc", "hevx", "mif1", "msf1"];

    /// <summary>Type MIME reconnu, ou null si le fichier n'est pas d'un type accepté.</summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP;
        }
        if (header.StartsWith("%PDF-"u8))
        {
            return Pdf;
        }
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8)
            && HeifBrands.Contains(Encoding.ASCII.GetString(header[8..12]), StringComparer.Ordinal))
        {
            return Heic;
        }
        return null;
    }

    /// <summary>Images affichées dans le navigateur ; les autres types sont proposés au téléchargement.</summary>
    public static bool IsInline(string contentType) => contentType is Jpeg or Png or WebP;
}
