namespace HomeBudget.Api.Domain.Notifications;

/// <summary>Fréquence du résumé par e-mail (M2).</summary>
public enum EmailSummaryFrequency
{
    None,

    /// <summary>Chaque lundi, sur la semaine écoulée.</summary>
    Weekly,

    /// <summary>Le 1er du mois, sur le mois écoulé.</summary>
    Monthly,
}

/// <summary>
/// Choix de notifications d'un utilisateur (M1, M2) : les types d'alertes push reçus, et la fréquence du
/// résumé par e-mail. Sans ligne, les valeurs par défaut s'appliquent : toutes les alertes, pas de résumé.
/// </summary>
public sealed class NotificationPreference : Entity
{
    public Guid UserId { get; set; }

    public bool BudgetAlerts { get; set; } = true;

    public bool ForecastAlerts { get; set; } = true;

    public bool DueTomorrowAlerts { get; set; } = true;

    public bool PendingAlerts { get; set; } = true;

    public EmailSummaryFrequency EmailSummary { get; set; } = EmailSummaryFrequency.None;

    public bool Allows(AlertKind kind) => kind switch
    {
        AlertKind.BudgetWarning or AlertKind.BudgetExceeded => BudgetAlerts,
        AlertKind.ForecastBelowThreshold => ForecastAlerts,
        AlertKind.DueTomorrow => DueTomorrowAlerts,
        AlertKind.PendingValidation => PendingAlerts,
        _ => false,
    };
}
