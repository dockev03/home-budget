namespace HomeBudget.Api.Domain.Attachments;

/// <summary>
/// Pièce jointe d'une opération (B7) : photo d'un ticket, facture, fiche de paie. Le fichier est stocké une seule
/// fois sous son empreinte SHA-256 (<c>/data/attachments/{sha256}</c>) et n'est jamais modifié ; deux opérations
/// peuvent partager le même fichier. La pièce jointe suit la visibilité de son opération (R8).
/// </summary>
public sealed class Attachment : AuditableEntity
{
    public const int MaxFileNameLength = 200;
    public const int MaxContentTypeLength = 64;
    public const int HashLength = 64;

    /// <summary>Taille maximale d'un fichier (10 Mio).</summary>
    public const long MaxSize = 10L * 1024 * 1024;

    /// <summary>Nombre maximal de pièces jointes par opération.</summary>
    public const int MaxPerTransaction = 20;

    public Guid TransactionId { get; set; }

    /// <summary>Nom d'origine du fichier, affiché et proposé au téléchargement.</summary>
    public required string FileName { get; set; }

    /// <summary>Type reconnu sur les premiers octets du fichier, jamais celui annoncé par le client.</summary>
    public required string ContentType { get; set; }

    public long Size { get; set; }

    /// <summary>Empreinte SHA-256 du contenu, en hexadécimal minuscule : nom du fichier sur le disque.</summary>
    public required string Sha256 { get; set; }
}
