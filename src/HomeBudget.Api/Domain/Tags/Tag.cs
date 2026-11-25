using HomeBudget.Api.Domain.Payees;

namespace HomeBudget.Api.Domain.Tags;

/// <summary>Tag libre (C4), posé sur les lignes d'opération et créé à la volée lors de la saisie.</summary>
public sealed class Tag : AuditableEntity
{
    public const int MaxNameLength = 30;

    /// <summary>Nombre maximal de tags sur une ligne d'opération.</summary>
    public const int MaxPerLine = 10;

    public required string Name { get; set; }

    /// <summary>Nom normalisé (majuscules, espaces réduits) : un même tag quelle que soit la casse.</summary>
    public required string NormalizedName { get; set; }

    public static string Normalize(string name) => Payee.Normalize(name);

    /// <summary>Nom affiché : espaces superflus retirés.</summary>
    public static string Clean(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
