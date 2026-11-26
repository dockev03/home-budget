namespace HomeBudget.Api.Domain.Budgets;

/// <summary>
/// Provision (D11) : une dépense annuelle ou ponctuelle, lissée sur les périodes qui la précèdent. Chaque période
/// réserve sa part dans le budget de la catégorie ; la période de l'échéance dispose du montant entier.
/// </summary>
public sealed class Provision : AuditableEntity
{
    public const int MaxLabelLength = 100;

    public required string Label { get; set; }

    /// <summary>Catégorie de dépense qui recevra la dépense.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Montant de la dépense, positif.</summary>
    public decimal Amount { get; set; }

    /// <summary>Date de la dépense ; pour une provision annuelle, celle de la première échéance.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Annuelle : la dépense revient chaque année, lissée sur les 12 périodes qui y mènent.</summary>
    public bool IsAnnual { get; set; }

    /// <summary>Début du lissage : aucune part n'est réservée avant la période de cette date.</summary>
    public DateOnly StartDate { get; set; }
}
