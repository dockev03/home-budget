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
    decimal SafetyThreshold);

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
/// <param name="ThresholdDate">Premier jour où le solde passe sous le seuil (aujourd'hui s'il y est déjà).</param>
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
    RemainingToLive RemainingToLive);

/// <summary>
/// Moteur de prévision de la période en cours (F1, F2, F3, F6 ; R3, R4), sans accès à la base.
/// </summary>
/// <remarks>
/// Solde prévu = solde réel à J + opérations saisies après J + échéances restantes
/// + Σ par catégorie principale de max(0, budget restant − échéances restantes), en négatif pour une dépense.
/// Les revenus budgétés sont exclus si <see cref="ForecastInput.IncludeIncome"/> est faux. Le reste du budget
/// est étalé uniformément sur les jours restants (J+1 à la fin de la période) pour tracer la courbe.
/// Le dernier jour de la période, il ne reste plus de jour à prévoir : seules les échéances à valider en
/// retard s'ajoutent alors au solde de fin.
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
        var budget = BudgetRemainders(input, categories);

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
            daily.Where(p => p.Date >= today && p.Balance < input.Threshold).Select(p => (DateOnly?)p.Date).FirstOrDefault(),
            expected,
            new RemainingToLive(
                rav,
                Math.Round(rav / daysLeft, 2, MidpointRounding.ToZero),
                daysLeft,
                expected.Income,
                expected.Fixed,
                expected.Savings,
                input.SafetyThreshold));
    }

    private readonly record struct BudgetRemainder(CategoryKind Kind, decimal Signed);

    /// <summary>Part du budget restant non couverte par une échéance, par catégorie principale (signée).</summary>
    private static List<BudgetRemainder> BudgetRemainders(ForecastInput input, Dictionary<Guid, ForecastCategory> categories)
    {
        var result = new List<BudgetRemainder>();
        foreach (var b in input.Budgets)
        {
            if (!categories.TryGetValue(b.CategoryId, out var category) || (category.Kind == CategoryKind.Income && !input.IncludeIncome))
            {
                continue;
            }
            var sign = Sign(category.Kind);
            var scheduled = input.ScheduledByCategory
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
