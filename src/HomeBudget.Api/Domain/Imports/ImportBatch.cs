namespace HomeBudget.Api.Domain.Imports;

/// <summary>
/// Import d'un relevé dans un compte (H1 à H3) : trace du fichier et du résultat. La date et l'auteur sont
/// ceux de la création de l'entité.
/// </summary>
public sealed class ImportBatch : AuditableEntity
{
    public Guid AccountId { get; set; }

    public required string FileName { get; set; }

    public ImportFormat Format { get; set; }

    /// <summary>Opérations créées (échéances rapprochées comprises).</summary>
    public int Created { get; set; }

    /// <summary>Opérations qui ont remplacé une échéance de récurrence (E6).</summary>
    public int Matched { get; set; }

    /// <summary>Opérations déjà présentes, écartées.</summary>
    public int Duplicates { get; set; }
}
