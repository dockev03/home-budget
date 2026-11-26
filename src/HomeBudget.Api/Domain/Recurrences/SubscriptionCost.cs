namespace HomeBudget.Api.Domain.Recurrences;

/// <summary>Coût d'un abonnement (E10) ramené à l'année et au mois, quelle que soit sa fréquence.</summary>
public static class SubscriptionCost
{
    /// <summary>Montant sur un an : montant × nombre d'échéances par an (365 jours, 52 semaines, 12 mois).</summary>
    public static decimal Annual(decimal amount, FrequencyUnit unit, int interval)
    {
        var perYear = unit switch
        {
            FrequencyUnit.Day => 365m,
            FrequencyUnit.Week => 52m,
            FrequencyUnit.Month => 12m,
            FrequencyUnit.Year => 1m,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };
        return decimal.Round(amount * perYear / Math.Max(interval, 1), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Montant mensuel moyen : le douzième du montant annuel.</summary>
    public static decimal Monthly(decimal amount, FrequencyUnit unit, int interval) =>
        decimal.Round(Annual(amount, unit, interval) / 12m, 2, MidpointRounding.AwayFromZero);
}
