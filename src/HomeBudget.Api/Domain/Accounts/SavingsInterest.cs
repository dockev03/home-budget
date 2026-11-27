namespace HomeBudget.Api.Domain.Accounts;

/// <summary>Mouvement d'un compte d'épargne à sa date d'opération (le solde initial en est un, à la date d'ouverture).</summary>
public readonly record struct SavingsMovement(DateOnly Date, decimal Amount);

/// <summary>Taux annuel en pourcentage, en vigueur à partir d'une date.</summary>
public readonly record struct SavingsRatePeriod(DateOnly EffectiveFrom, decimal AnnualRate);

/// <summary>Une quinzaine de l'année et ses intérêts.</summary>
/// <param name="Base">Solde rémunéré : solde de fin de la quinzaine précédente, moins les retraits de celle-ci (0 au plus bas).</param>
/// <param name="AnnualRate">Taux en vigueur le premier jour de la quinzaine.</param>
/// <param name="Interest">Base × taux / 24, non arrondi.</param>
public readonly record struct InterestFortnight(DateOnly Start, DateOnly End, decimal Base, decimal AnnualRate, decimal Interest);

/// <summary>
/// Intérêts d'un livret sur une année civile (A9), selon la règle des quinzaines des livrets réglementés. L'année
/// compte 24 quinzaines (du 1er au 15 et du 16 à la fin du mois), qui rapportent chacune taux annuel / 24, quelle que
/// soit leur durée. Un dépôt rapporte à partir de la quinzaine suivante ; un retrait cesse de rapporter dès le début
/// de sa quinzaine. Le taux d'une quinzaine est celui en vigueur à son premier jour : un taux fixé en cours de
/// quinzaine s'applique à partir de la suivante. Les intérêts de l'année sont capitalisés au 31 décembre par la
/// banque : ils ne rapportent ici qu'une fois l'opération correspondante enregistrée sur le compte.
/// </summary>
public sealed class SavingsInterest
{
    public const int FortnightsPerYear = 24;

    private SavingsInterest(int year, IReadOnlyList<InterestFortnight> fortnights)
    {
        Year = year;
        Fortnights = fortnights;
    }

    public int Year { get; }

    public IReadOnlyList<InterestFortnight> Fortnights { get; }

    /// <summary>Intérêts de toute l'année, arrondis au centime.</summary>
    public decimal Total => Round(Fortnights.Sum(f => f.Interest));

    /// <summary>Intérêts acquis à la fin du jour <paramref name="date"/> : ceux des quinzaines terminées, arrondis au centime.</summary>
    public decimal EarnedAt(DateOnly date) => Round(Fortnights.Where(f => f.End <= date).Sum(f => f.Interest));

    /// <param name="movements">Tous les mouvements du compte, solde initial compris ; ceux des années suivantes sont ignorés.</param>
    /// <param name="rates">Taux du compte ; avant le premier, le taux est nul.</param>
    public static SavingsInterest Compute(int year, IEnumerable<SavingsMovement> movements, IEnumerable<SavingsRatePeriod> rates)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, DateOnly.MinValue.Year + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, DateOnly.MaxValue.Year);

        var sortedRates = rates.OrderBy(r => r.EffectiveFrom).ToList();
        var firstDay = new DateOnly(year, 1, 1);
        var moves = movements.Where(m => m.Date.Year <= year).ToList();
        // Solde à la fin de la quinzaine précédente (au départ, au 31 décembre de l'année d'avant).
        var balance = moves.Where(m => m.Date < firstDay).Sum(m => m.Amount);

        var fortnights = new List<InterestFortnight>(FortnightsPerYear);
        foreach (var (start, end) in FortnightsOf(year))
        {
            var inside = moves.Where(m => m.Date >= start && m.Date <= end).ToList();
            var withdrawals = inside.Where(m => m.Amount < 0).Sum(m => m.Amount);
            var basis = Math.Max(0m, balance + withdrawals);
            var rate = RateAt(sortedRates, start);
            fortnights.Add(new InterestFortnight(start, end, basis, rate, basis * rate / 100m / FortnightsPerYear));
            balance += inside.Sum(m => m.Amount);
        }
        return new SavingsInterest(year, fortnights);
    }

    /// <summary>Les 24 quinzaines de l'année : du 1er au 15, puis du 16 au dernier jour de chaque mois.</summary>
    public static IEnumerable<(DateOnly Start, DateOnly End)> FortnightsOf(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            yield return (new DateOnly(year, month, 1), new DateOnly(year, month, 15));
            yield return (new DateOnly(year, month, 16), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
        }
    }

    /// <summary>Taux en vigueur le jour <paramref name="date"/> (0 avant le premier taux).</summary>
    public static decimal RateAt(IEnumerable<SavingsRatePeriod> rates, DateOnly date) =>
        rates.Where(r => r.EffectiveFrom <= date).OrderBy(r => r.EffectiveFrom).Select(r => r.AnnualRate).LastOrDefault();

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
