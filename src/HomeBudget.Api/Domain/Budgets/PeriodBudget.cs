namespace HomeBudget.Api.Domain.Budgets;

/// <summary>
/// Budget d'une catégorie pour une seule période (D4) : il remplace, pour cette période, le budget par défaut
/// (<see cref="CategoryBudget"/>) ou le mode appliqué par défaut.
/// </summary>
public sealed class PeriodBudget : AuditableEntity
{
    public Guid CategoryId { get; set; }

    /// <summary>Clé <c>AAAA-MM</c> de la période.</summary>
    public required string PeriodKey { get; set; }

    public BudgetMode Mode { get; set; }

    /// <summary>Montant en euros (mode Amount) ou pourcentage (mode PercentOfIncome) ; 0 en mode Auto.</summary>
    public decimal Value { get; set; }
}
