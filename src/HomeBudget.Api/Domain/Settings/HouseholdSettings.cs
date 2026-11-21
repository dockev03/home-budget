namespace HomeBudget.Api.Domain.Settings;

public enum PeriodMode
{
    /// <summary>Du 1er au dernier jour du mois.</summary>
    Calendar,

    /// <summary>Du jour N au jour N-1 du mois suivant.</summary>
    FixedDay,
}

/// <summary>Paramètres du foyer (une seule ligne par instance).</summary>
public sealed class HouseholdSettings : AuditableEntity
{
    public PeriodMode PeriodMode { get; set; } = PeriodMode.Calendar;

    /// <summary>Jour de début de période en mode <see cref="PeriodMode.FixedDay"/> (1 à 28).</summary>
    public int PeriodStartDay { get; set; } = 1;

    /// <summary>Seuil de sécurité retenu dans le calcul du reste à vivre.</summary>
    public decimal SafetyThreshold { get; set; }

    /// <summary>Inclure les revenus budgétés non encore reçus dans la prévision.</summary>
    public bool ForecastIncludesIncome { get; set; } = true;

    public int BudgetWarningPercent { get; set; } = 80;

    public int BudgetAlertPercent { get; set; } = 100;

    /// <summary>Le jeu de catégories par défaut (C6) a été créé : il ne l'est qu'une fois.</summary>
    public bool DefaultCategoriesCreated { get; set; }
}
