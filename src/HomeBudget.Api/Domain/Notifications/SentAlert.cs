namespace HomeBudget.Api.Domain.Notifications;

public enum AlertKind
{
    /// <summary>Budget d'une catégorie consommé au seuil d'avertissement (80 % par défaut).</summary>
    BudgetWarning,

    /// <summary>Budget d'une catégorie consommé au seuil d'alerte (100 % par défaut).</summary>
    BudgetExceeded,

    /// <summary>Passage prévu sous le seuil des comptes de la prévision (F3).</summary>
    ForecastBelowThreshold,

    /// <summary>Échéance d'une récurrence demain.</summary>
    DueTomorrow,

    /// <summary>Échéance à valider (E5).</summary>
    PendingValidation,

    /// <summary>Résumé par e-mail (M2).</summary>
    EmailSummary,
}

/// <summary>
/// Alerte déjà envoyée à un utilisateur : une même alerte, identifiée par (type, objet, période), ne part
/// qu'une fois. L'objet est une catégorie, une échéance ou une récurrence ; la période, une clé AAAA-MM, une date
/// ou une semaine.
/// </summary>
public sealed class SentAlert : Entity
{
    public const int MaxKeyLength = 64;

    public Guid UserId { get; set; }

    public AlertKind Kind { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public string PeriodKey { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}
