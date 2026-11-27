namespace HomeBudget.Api.Domain.Payees;

/// <summary>Organisme qui rembourse les dépenses de santé (P4).</summary>
public enum Reimburser
{
    None,

    /// <summary>Assurance maladie obligatoire (CPAM).</summary>
    PrimaryInsurance,

    /// <summary>Complémentaire santé (mutuelle).</summary>
    Mutual,
}

/// <summary>Tiers d'une opération (B8), créé à la volée lors de la saisie.</summary>
public sealed class Payee : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>Nom normalisé (majuscules, espaces réduits) : unicité insensible à la casse et aux accents près.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>Organisme de santé que ce tiers représente : ses remboursements sont classés à part (P4).</summary>
    public Reimburser Reimburser { get; set; }

    public static string Normalize(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
}
