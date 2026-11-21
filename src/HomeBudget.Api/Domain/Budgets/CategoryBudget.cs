namespace HomeBudget.Api.Domain.Budgets;

public enum BudgetMode
{
    /// <summary>Montant fixe en euros.</summary>
    Amount,

    /// <summary>Pourcentage de la somme des revenus budgétés de la période.</summary>
    PercentOfIncome,

    /// <summary>Somme des échéances de récurrence de la période.</summary>
    Auto,
}

/// <summary>Budget par défaut d'une catégorie, appliqué à chaque période (D1, D2).</summary>
public sealed class CategoryBudget : AuditableEntity
{
    public Guid CategoryId { get; set; }

    public BudgetMode Mode { get; set; }

    /// <summary>Montant en euros (mode Amount) ou pourcentage (mode PercentOfIncome) ; 0 en mode Auto.</summary>
    public decimal Value { get; set; }
}
