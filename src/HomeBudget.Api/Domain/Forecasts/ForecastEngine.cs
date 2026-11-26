using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;

namespace HomeBudget.Api.Domain.Forecasts;

/// <summary>Mouvement signé (négatif = sortie) daté, avec sa catégorie éventuelle.</summary>
public readonly record struct ForecastMovement(DateOnly Date, decimal Amount, Guid? CategoryId);

public sealed record ForecastCategory(Guid Id, Guid? ParentId, CategoryKind Kind);

/// <summary>Budget restant d'une catégorie principale (sous-catégories comprises) : budget − réalisé, orienté.</summary>
public readonly record struct ForecastBudget(Guid CategoryId, decimal Remaining);

/// <summary>Entrées de la prévision de la période en cours.</summary>
/// <param name="OpeningBalance">Solde des comptes de la prévision au début de la période.</param>
/// <param name="Transactions">Opérations de la période sur ces comptes, passées et futures (B4).</param>
/// <param name="Scheduled">
/// Échéances restantes (R3), à leur date d'effet, par leur effet sur ces comptes. Une échéance à valider
/// en retard garde sa date passée : elle est reportée au premier jour de prévision.
/// </param>
/// <param name="ScheduledByCategory">
/// Échéances restantes de la période sur tous les comptes, par catégorie (somme signée) : elles couvrent
/// une partie du budget restant.
/// </param>
/// <param name="Budgets">Budget restant par catégorie principale.</param>
/// <param name="Threshold">Seuil d'alerte des comptes de la prévision (F3).</param>
/// <param name="SafetyThreshold">Seuil de sécurité du reste à vivre (R4).</param>
/// <param name="FuturePeriods">Périodes suivantes de l'horizon (F4), dans l'ordre ; aucune par défaut.</param>
/// <param name="ScenarioMovements">
/// Mouvements fictifs des scénarios demandés (F5), sur les comptes de la prévision. Seuls ceux datés d'aujourd'hui
/// à la fin de l'horizon comptent ; ils forment une série à part et ne touchent jamais la prévision de base.
/// </param>
public sealed record ForecastInput(
    Period Period,
    DateOnly Today,
    decimal OpeningBalance,
    IReadOnlyList<ForecastMovement> Transactions,
    IReadOnlyList<ForecastMovement> Scheduled,
    IReadOnlyDictionary<Guid, decimal> ScheduledByCategory,
    IReadOnlyList<ForecastCategory> Categories,
    IReadOnlyList<ForecastBudget> Budgets,
    bool IncludeIncome,
    decimal Threshold,
    decimal SafetyThreshold,
    IReadOnlyList<ForecastFuturePeriod>? FuturePeriods = null,
    IReadOnlyList<ForecastMovement>? ScenarioMovements = null);

/// <summary>Période à venir de l'horizon (F4), prévue en entier.</summary>
/// <param name="Transactions">Opérations déjà saisies dans la période, sur les comptes de la prévision.</param>
/// <param name="Scheduled">Échéances de la période, par leur effet sur ces comptes.</param>
/// <param name="ScheduledByCategory">Échéances de la période sur tous les comptes, par catégorie (somme signée).</param>
/// <param name="Budgets">Reste par catégorie principale : budget (ou moyenne, F7) − réalisé déjà saisi.</param>
public sealed record ForecastFuturePeriod(
    Period Period,
    IReadOnlyList<ForecastMovement> Transactions,
    IReadOnlyList<ForecastMovement> Scheduled,
    IReadOnlyDictionary<Guid, decimal> ScheduledByCategory,
    IReadOnlyList<ForecastBudget> Budgets);

/// <summary>Solde prévu en fin d'une période de l'horizon.</summary>
/// <param name="ScenarioEndBalance">Même solde avec les scénarios demandés (null sans scénario).</param>
public sealed record ForecastPeriodEnd(Period Period, decimal EndBalance, decimal? ScenarioEndBalance);

/// <summary>Prévision avec les scénarios demandés (F5).</summary>
/// <param name="Daily">Solde jour par jour, d'aujourd'hui à la fin de l'horizon.</param>
/// <param name="Impact">Effet total des scénarios sur l'horizon.</param>
/// <param name="ThresholdDate">Premier jour sous le seuil avec les scénarios.</param>
public sealed record ScenarioForecast(IReadOnlyList<ForecastPoint> Daily, decimal EndBalance, decimal Impact, DateOnly? ThresholdDate);

/// <summary>Solde du jour : réel jusqu'à aujourd'hui, prévu ensuite (F2).</summary>
public readonly record struct ForecastPoint(DateOnly Date, decimal Balance, bool IsForecast);

/// <summary>Ce qui reste attendu sur la période, par nature (montants positifs).</summary>
public sealed record ExpectedByKind(decimal Income, decimal Fixed, decimal Variable, decimal Savings);

/// <param name="Total">Reste à vivre sur la période (R4).</param>
/// <param name="PerDay">Reste à vivre par jour restant, aujourd'hui compris.</param>
public sealed record RemainingToLive(
    decimal Total, decimal PerDay, int DaysLeft, decimal Income, decimal Fixed, decimal Savings, decimal SafetyThreshold);

/// <param name="FutureTransactions">Opérations déjà saisies, datées après aujourd'hui.</param>
/// <param name="Scheduled">Échéances de récurrence restantes.</param>
/// <param name="Budget">Budget restant non couvert par une échéance, étalé sur les jours restants.</param>
/// <param name="Daily">Solde jour par jour, du début de la période en cours à la fin de l'horizon.</param>
/// <param name="ThresholdDate">
/// Premier jour de l'horizon où le solde passe sous le seuil (aujourd'hui s'il y est déjà).
/// </param>
/// <param name="Periods">Solde prévu en fin de chaque période de l'horizon, la période en cours d'abord.</param>
/// <param name="Scenario">Prévision avec les scénarios (null sans scénario demandé).</param>
public sealed record ForecastResult(
    decimal CurrentBalance,
    decimal EndBalance,
    decimal FutureTransactions,
    decimal Scheduled,
    decimal Budget,
    IReadOnlyList<ForecastPoint> Daily,
    decimal Threshold,
    DateOnly? ThresholdDate,
    ExpectedByKind Expected,
    RemainingToLive RemainingToLive,
    IReadOnlyList<ForecastPeriodEnd> Periods,
    ScenarioForecast? Scenario);

/// <summary>
/// Moteur de prévision (F1 à F6 ; R3, R4), sans accès à la base : la période en cours, puis les périodes
/// suivantes de l'horizon (F4), et une série à part pour les scénarios (F5).
/// </summary>
/// <remarks>
/// Solde prévu = solde réel à J + opérations saisies après J + échéances restantes
/// + Σ par catégorie principale de max(0, budget restant − échéances restantes), en négatif pour une dépense.
/// Les revenus budgétés sont exclus si <see cref="ForecastInput.IncludeIncome"/> est faux. Le reste du budget
/// est étalé uniformément sur les jours restants (J+1 à la fin de la période) pour tracer la courbe.
/// Le dernier jour de la période, il ne reste plus de jour à prévoir : seules les échéances à valider en
/// retard s'ajoutent alors au solde de fin.
/// Une période suivante part du solde de fin de la précédente : opérations déjà saisies, échéances, et reste du
/// budget (ou de la moyenne, F7) étalé sur tous ses jours, selon la même règle. Le reste à vivre ne porte que sur
/// la période en cours.
/// </remarks>
public static class ForecastEngine
{
    public static ForecastResult Compute(ForecastInput input)
    {
        var period = input.Period;
        if (!period.Contains(input.Today))
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Today, "La prévision porte sur la période qui contient aujourd'hui.");
        }
        var today = input.Today;
        var firstForecastDay = today.AddDays(1);
        var forecastDays = period.End.DayNumber - today.DayNumber;
        var categories = input.Categories.ToDictionary(c => c.Id);

        var future = input.Transactions.Where(t => t.Date > today).ToList();
        var scheduled = input.Scheduled.Where(s => s.Date <= period.End).ToList();
        var budget = BudgetRemainders(input.Budgets, input.ScheduledByCategory, categories, input.IncludeIncome);

        // Mouvements prévus par jour : opérations futures, échéances (en retard : au premier jour), budget étalé.
        var byDay = new Dictionary<DateOnly, decimal>();
        void Add(DateOnly date, decimal amount) => byDay[date] = byDay.GetValueOrDefault(date) + amount;
        foreach (var t in future)
        {
            Add(t.Date, t.Amount);
        }
        foreach (var s in scheduled)
        {
            Add(s.Date > today ? s.Date : firstForecastDay, s.Amount);
        }
        var budgetTotal = forecastDays > 0 ? budget.Sum(b => b.Signed) : 0m;
        foreach (var (date, amount) in Spread(budgetTotal, firstForecastDay, forecastDays))
        {
            Add(date, amount);
        }

        var daily = new List<ForecastPoint>(period.Days);
        var balance = input.OpeningBalance;
        var realByDay = input.Transactions.Where(t => t.Date <= today).GroupBy(t => t.Date).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        for (var date = period.Start; date <= period.End; date = date.AddDays(1))
        {
            var isForecast = date > today;
            balance += isForecast ? byDay.GetValueOrDefault(date) : realByDay.GetValueOrDefault(date);
            daily.Add(new ForecastPoint(date, balance, isForecast));
        }
        var current = daily.First(p => p.Date == today).Balance;
        var scheduledTotal = scheduled.Sum(s => s.Amount);
        // Sans jour restant, les échéances en retard n'ont pas de jour où se placer : elles comptent dans le solde de fin.
        var endBalance = forecastDays > 0 ? daily[^1].Balance : current + scheduledTotal;

        var ends = new List<(Period Period, decimal End)> { (period, endBalance) };
        balance = endBalance;
        foreach (var next in input.FuturePeriods ?? [])
        {
            balance = AddFuturePeriod(next, balance, categories, input.IncludeIncome, daily);
            ends.Add((next.Period, balance));
        }
        var scenario = Scenario(input, daily, ends);

        var expected = Expected(future, scheduled, forecastDays > 0 ? budget : [], categories);
        var daysLeft = forecastDays + 1;
        var rav = current + expected.Income - expected.Fixed - expected.Savings - input.SafetyThreshold;

        return new ForecastResult(
            current,
            endBalance,
            future.Sum(t => t.Amount),
            scheduledTotal,
            budgetTotal,
            daily,
            input.Threshold,
            FirstBelow(daily, today, input.Threshold),
            expected,
            new RemainingToLive(
                rav,
                Math.Round(rav / daysLeft, 2, MidpointRounding.ToZero),
                daysLeft,
                expected.Income,
                expected.Fixed,
                expected.Savings,
                input.SafetyThreshold),
            [.. ends.Select(e => new ForecastPeriodEnd(e.Period, e.End, scenario is { } sc ? e.End + sc.CumulatedAt(e.Period.End) : null))],
            scenario?.Forecast);
    }

    /// <summary>Ajoute les jours d'une période à venir à la courbe ; renvoie son solde de fin.</summary>
    private static decimal AddFuturePeriod(
        ForecastFuturePeriod next, decimal opening, Dictionary<Guid, ForecastCategory> categories, bool includeIncome, List<ForecastPoint> daily)
    {
        var period = next.Period;
        var byDay = new Dictionary<DateOnly, decimal>();
        void Add(DateOnly date, decimal amount) => byDay[date] = byDay.GetValueOrDefault(date) + amount;
        foreach (var m in next.Transactions.Concat(next.Scheduled).Where(m => period.Contains(m.Date)))
        {
            Add(m.Date, m.Amount);
        }
        var budget = BudgetRemainders(next.Budgets, next.ScheduledByCategory, categories, includeIncome).Sum(b => b.Signed);
        foreach (var (date, amount) in Spread(budget, period.Start, period.Days))
        {
            Add(date, amount);
        }
        var balance = opening;
        for (var date = period.Start; date <= period.End; date = date.AddDays(1))
        {
            balance += byDay.GetValueOrDefault(date);
            daily.Add(new ForecastPoint(date, balance, true));
        }
        return balance;
    }

    /// <summary>
    /// Série « avec scénarios » : le solde prévu, plus l'effet cumulé des mouvements fictifs d'aujourd'hui à la fin
    /// de l'horizon. Les mouvements passés sont ignorés : un scénario porte sur l'avenir.
    /// </summary>
    /// <returns>La série, et l'effet cumulé des scénarios à une date donnée.</returns>
    private static (ScenarioForecast Forecast, Func<DateOnly, decimal> CumulatedAt)? Scenario(
        ForecastInput input, List<ForecastPoint> daily, List<(Period Period, decimal End)> ends)
    {
        if (input.ScenarioMovements is not { } movements)
        {
            return null;
        }
        var horizonEnd = ends[^1].Period.End;
        var steps = new List<(DateOnly Date, decimal Cumulated)>();
        foreach (var day in movements.Where(m => m.Date >= input.Today && m.Date <= horizonEnd).GroupBy(m => m.Date).OrderBy(g => g.Key))
        {
            steps.Add((day.Key, (steps.Count > 0 ? steps[^1].Cumulated : 0m) + day.Sum(m => m.Amount)));
        }
        decimal CumulatedAt(DateOnly date) => steps.LastOrDefault(s => s.Date <= date).Cumulated;

        var points = daily.Where(p => p.Date >= input.Today)
            .Select(p => new ForecastPoint(p.Date, p.Balance + CumulatedAt(p.Date), p.IsForecast))
            .ToList();
        var impact = steps.Count > 0 ? steps[^1].Cumulated : 0m;
        return (new ScenarioForecast(points, ends[^1].End + impact, impact, FirstBelow(points, input.Today, input.Threshold)), CumulatedAt);
    }

    private static DateOnly? FirstBelow(IEnumerable<ForecastPoint> daily, DateOnly today, decimal threshold) =>
        daily.Where(p => p.Date >= today && p.Balance < threshold).Select(p => (DateOnly?)p.Date).FirstOrDefault();

    private readonly record struct BudgetRemainder(CategoryKind Kind, decimal Signed);

    /// <summary>Part du budget restant non couverte par une échéance, par catégorie principale (signée).</summary>
    private static List<BudgetRemainder> BudgetRemainders(
        IReadOnlyList<ForecastBudget> budgets, IReadOnlyDictionary<Guid, decimal> scheduledByCategory,
        Dictionary<Guid, ForecastCategory> categories, bool includeIncome)
    {
        var result = new List<BudgetRemainder>();
        foreach (var b in budgets)
        {
            if (!categories.TryGetValue(b.CategoryId, out var category) || (category.Kind == CategoryKind.Income && !includeIncome))
            {
                continue;
            }
            var sign = Sign(category.Kind);
            var scheduled = scheduledByCategory
                .Where(s => s.Key == category.Id || (categories.TryGetValue(s.Key, out var c) && c.ParentId == category.Id))
                .Sum(s => sign * s.Value);
            var extra = Math.Max(0m, b.Remaining - scheduled);
            if (extra > 0)
            {
                result.Add(new BudgetRemainder(category.Kind, sign * extra));
            }
        }
        return result;
    }

    /// <summary>Répartit un montant au centime près sur <paramref name="days"/> jours ; le dernier jour reçoit l'arrondi.</summary>
    private static IEnumerable<(DateOnly Date, decimal Amount)> Spread(decimal total, DateOnly first, int days)
    {
        if (days <= 0 || total == 0)
        {
            yield break;
        }
        var perDay = Math.Round(total / days, 2, MidpointRounding.ToZero);
        for (var i = 0; i < days; i++)
        {
            yield return (first.AddDays(i), i == days - 1 ? total - perDay * (days - 1) : perDay);
        }
    }

    /// <summary>
    /// Ce qui reste attendu par nature : opérations futures, échéances et budget restant. Un mouvement sans
    /// catégorie compte comme un revenu s'il est positif, comme une dépense fixe sinon.
    /// </summary>
    private static ExpectedByKind Expected(
        IEnumerable<ForecastMovement> future, IEnumerable<ForecastMovement> scheduled, IEnumerable<BudgetRemainder> budget,
        Dictionary<Guid, ForecastCategory> categories)
    {
        var sums = new Dictionary<CategoryKind, decimal>();
        void Add(CategoryKind kind, decimal signed) => sums[kind] = sums.GetValueOrDefault(kind) + Sign(kind) * signed;
        foreach (var m in future.Concat(scheduled))
        {
            var kind = m.CategoryId is { } id && categories.TryGetValue(id, out var c) ? c.Kind
                : m.Amount > 0 ? CategoryKind.Income : CategoryKind.Fixed;
            Add(kind, m.Amount);
        }
        foreach (var b in budget)
        {
            Add(b.Kind, b.Signed);
        }
        return new ExpectedByKind(
            sums.GetValueOrDefault(CategoryKind.Income),
            sums.GetValueOrDefault(CategoryKind.Fixed),
            sums.GetValueOrDefault(CategoryKind.Variable),
            sums.GetValueOrDefault(CategoryKind.Savings));
    }

    private static decimal Sign(CategoryKind kind) => kind == CategoryKind.Income ? 1m : -1m;
}
