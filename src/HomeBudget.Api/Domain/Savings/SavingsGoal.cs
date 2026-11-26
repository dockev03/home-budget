namespace HomeBudget.Api.Domain.Savings;

/// <summary>
/// Objectif d'épargne (J1) : un montant cible, une date cible facultative, et ce qui mesure l'épargne réunie, soit
/// le solde d'un compte (par exemple un livret dédié à un enfant, J4), soit le cumul d'une catégorie d'épargne depuis
/// <see cref="StartDate"/>. Rattaché à un compte, il suit la visibilité de ce compte (R8).
/// </summary>
public sealed class SavingsGoal : AuditableEntity
{
    public const int MaxNameLength = 100;

    public required string Name { get; set; }

    /// <summary>Montant à réunir, positif.</summary>
    public decimal TargetAmount { get; set; }

    public DateOnly? TargetDate { get; set; }

    /// <summary>Compte dont le solde mesure l'épargne réunie (exclusif de <see cref="CategoryId"/>).</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Catégorie d'épargne dont le cumul depuis <see cref="StartDate"/> mesure l'épargne réunie.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Début du cumul de la catégorie (ignoré pour un objectif rattaché à un compte).</summary>
    public DateOnly StartDate { get; set; }

    public string? Color { get; set; }

    public string? Icon { get; set; }

    public bool IsArchived { get; set; }
}
