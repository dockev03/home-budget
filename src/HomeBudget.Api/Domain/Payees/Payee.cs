namespace HomeBudget.Api.Domain.Payees;

/// <summary>Tiers d'une opération (B8), créé à la volée lors de la saisie.</summary>
public sealed class Payee : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>Nom normalisé (majuscules, espaces réduits) : unicité insensible à la casse et aux accents près.</summary>
    public required string NormalizedName { get; set; }

    public static string Normalize(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
}
