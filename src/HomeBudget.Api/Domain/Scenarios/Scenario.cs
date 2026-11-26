namespace HomeBudget.Api.Domain.Scenarios;

/// <summary>
/// Scénario « et si… » (F5) : des événements fictifs, nommés, ajoutés à la prévision sur demande. Ils ne sont
/// jamais mélangés aux données réelles : ni opérations, ni budgets, ni totaux, ni exports.
/// </summary>
public sealed class Scenario : AuditableEntity
{
    public const int MaxNameLength = 100;

    public required string Name { get; set; }

    /// <summary>Actif : proposé par défaut sur la courbe du tableau de bord.</summary>
    public bool IsActive { get; set; } = true;

    public List<ScenarioEvent> Events { get; set; } = [];
}

public enum ScenarioEventFrequency
{
    /// <summary>Une seule fois, à la date de l'événement.</summary>
    Once,

    /// <summary>Chaque mois à partir de la date de l'événement, jusqu'à la date de fin éventuelle.</summary>
    Monthly,
}

/// <summary>Mouvement fictif d'un scénario, sur un compte (F5). Sa visibilité suit celle du compte (R8).</summary>
public sealed class ScenarioEvent : AuditableEntity
{
    public const int MaxLabelLength = 100;

    public Guid ScenarioId { get; set; }

    public required string Label { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>Catégorie indicative : elle ne touche ni au budget ni aux totaux.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Montant signé : négatif pour une sortie.</summary>
    public decimal Amount { get; set; }

    /// <summary>Date de l'événement ; pour un événement mensuel, celle de la première fois.</summary>
    public DateOnly Date { get; set; }

    public ScenarioEventFrequency Frequency { get; set; }

    /// <summary>Dernier jour possible d'un événement mensuel (null : sans fin).</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Dates de l'événement entre deux dates incluses. Un événement mensuel est calculé depuis sa date de départ
    /// (le 31 donne ensuite 28 ou 29, puis 31), comme une récurrence (R6).
    /// </summary>
    public IEnumerable<DateOnly> DatesBetween(DateOnly from, DateOnly to)
    {
        if (Frequency == ScenarioEventFrequency.Once)
        {
            if (Date >= from && Date <= to)
            {
                yield return Date;
            }
            yield break;
        }
        var last = EndDate is { } end && end < to ? end : to;
        for (var k = 0; ; k++)
        {
            var date = Date.AddMonths(k);
            if (date > last)
            {
                yield break;
            }
            if (date >= from)
            {
                yield return date;
            }
        }
    }
}
