using HomeBudget.Api.Domain.Periods;

namespace HomeBudget.Api.Domain.Budgets;

/// <summary>Part d'une provision pour une période.</summary>
/// <param name="Share">Part réservée dans le budget de la période (la dernière part reçoit l'arrondi).</param>
/// <param name="IsDue">Période de l'échéance : la catégorie dispose alors du montant entier.</param>
/// <param name="Saved">Cumul des parts réservées du cycle, cette période comprise.</param>
/// <param name="Periods">Nombre de périodes du cycle de lissage.</param>
/// <param name="DueKey">Clé de la période de l'échéance du cycle.</param>
public readonly record struct ProvisionShare(decimal Share, bool IsDue, decimal Saved, int Periods, string DueKey);

/// <summary>
/// Lissage d'une provision (D11), sans accès à la base. Les périodes sont numérotées par leur clé
/// <c>AAAA-MM</c> : chaque mois nomme exactement une période (R1), quel que soit le mode de période.
/// </summary>
/// <remarks>
/// Ponctuelle : de la période du début à celle de l'échéance. Annuelle : les 12 périodes qui mènent à chaque
/// échéance anniversaire, sans remonter avant la période du début (le premier cycle peut être plus court).
/// </remarks>
public static class ProvisionPlan
{
    public const int PeriodsPerYear = 12;

    /// <summary>Part de la provision pour la période ; null si la période est hors du lissage.</summary>
    public static ProvisionShare? For(
        decimal amount, Period start, Period due, bool isAnnual, Period period)
    {
        var p = Index(period);
        var s = Index(start);
        var d = Index(due);
        if (p < s)
        {
            return null;
        }
        if (isAnnual)
        {
            // Échéance du cycle qui contient la période : la prochaine qui tombe le même mois que la première.
            d = p + ((((d - p) % PeriodsPerYear) + PeriodsPerYear) % PeriodsPerYear);
        }
        else if (p > d)
        {
            return null;
        }
        var first = isAnnual ? Math.Max(s, d - PeriodsPerYear + 1) : s;
        var count = d - first + 1;
        var share = Math.Round(amount / count, 2, MidpointRounding.ToZero);
        var rank = p - first + 1;
        var isDue = p == d;
        var dueYear = Math.DivRem(d, PeriodsPerYear, out var dueMonth);
        return new ProvisionShare(
            isDue ? amount - (share * (count - 1)) : share,
            isDue,
            isDue ? amount : share * rank,
            count,
            $"{dueYear:D4}-{dueMonth + 1:D2}");
    }

    private static int Index(Period period) => (period.Year * PeriodsPerYear) + period.Month - 1;
}
