namespace HomeBudget.Api.Domain.Settings;

public enum PeriodMode
{
    /// <summary>Du 1er au dernier jour du mois.</summary>
    Calendar,

    /// <summary>Du jour N au jour N-1 du mois suivant.</summary>
    FixedDay,
}

/// <summary>Ce qui remplace le budget restant dans la prévision (F7).</summary>
public enum ForecastStrategy
{
    /// <summary>Le budget de chaque période (R3).</summary>
    Budget,

    /// <summary>Le réalisé moyen des N dernières périodes clôturées, par catégorie.</summary>
    Average,
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

    /// <summary>Stratégie de prévision (F7) : par le budget, ou par la moyenne des N dernières périodes.</summary>
    public ForecastStrategy ForecastStrategy { get; set; } = ForecastStrategy.Budget;

    /// <summary>Nombre de périodes clôturées de la moyenne (F7), de 1 à <see cref="MaxForecastAverageMonths"/>.</summary>
    public int ForecastAverageMonths { get; set; } = DefaultForecastAverageMonths;

    public const int DefaultForecastAverageMonths = 6;

    public const int MaxForecastAverageMonths = 24;

    /// <summary>Nombre de périodes clôturées de la dépense moyenne de l'épargne de précaution (J3), de 1 à <see cref="MaxForecastAverageMonths"/>.</summary>
    public int EmergencyFundAverageMonths { get; set; } = DefaultForecastAverageMonths;

    /// <summary>Répartition des dépenses communes (K4) : à parts égales, au prorata des revenus ou selon des parts fixes.</summary>
    public Split.SplitMode SplitMode { get; set; } = Split.SplitMode.Equal;

    public int BudgetWarningPercent { get; set; } = 80;

    public int BudgetAlertPercent { get; set; } = 100;

    /// <summary>
    /// Mode enveloppes (D7) : clé <c>AAAA-MM</c> de la période d'activation, à partir de laquelle « à affecter »
    /// est cumulé et le reste de chaque dépense est reporté ; null si le mode est désactivé.
    /// </summary>
    public string? EnvelopeSince { get; set; }

    /// <summary>Le jeu de catégories par défaut (C6) a été créé : il ne l'est qu'une fois.</summary>
    public bool DefaultCategoriesCreated { get; set; }
}
