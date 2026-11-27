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
/// Choix de notifications d'un utilisateur (M1, M2, N9) : les types d'alertes push reçus, la fréquence du résumé
/// par e-mail et les alertes de sécurité par e-mail. Sans ligne, les valeurs par défaut s'appliquent : toutes les
/// alertes push, pas de résumé, alerte de verrouillage mais pas d'alerte de connexion.
/// </summary>
public sealed class NotificationPreference : Entity
{
    public Guid UserId { get; set; }

    public bool BudgetAlerts { get; set; } = true;

    public bool ForecastAlerts { get; set; } = true;

    public bool DueTomorrowAlerts { get; set; } = true;

    public bool PendingAlerts { get; set; } = true;

    public EmailSummaryFrequency EmailSummary { get; set; } = EmailSummaryFrequency.None;

    /// <summary>E-mail à chaque nouvelle connexion (N9), désactivé par défaut.</summary>
    public bool LoginEmailAlert { get; set; }

    /// <summary>E-mail quand le compte est verrouillé après trop d'échecs (N9), activé par défaut.</summary>
    public bool LockoutEmailAlert { get; set; } = true;

    public bool Allows(AlertKind kind) => kind switch
    {
        AlertKind.BudgetWarning or AlertKind.BudgetExceeded => BudgetAlerts,
        AlertKind.ForecastBelowThreshold => ForecastAlerts,
        AlertKind.DueTomorrow => DueTomorrowAlerts,
        AlertKind.PendingValidation => PendingAlerts,
        _ => false,
    };
}
