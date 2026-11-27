using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Forecasts;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Forecasts;

/// <summary>Rassemble les données de l'horizon de prévision et appelle <see cref="ForecastEngine"/>.</summary>
public sealed class ForecastService(AppDbContext db, TimeProvider clock, IBudgetScheduleSource schedule)
{
    /// <summary>Horizons proposés (F4), en nombre de périodes, la période en cours comprise.</summary>
    public static readonly IReadOnlyList<int> Horizons = [1, 3, 6, 12];

    /// <param name="AverageMonths">Nombre de périodes de la moyenne (stratégie <see cref="ForecastStrategy.Average"/>).</param>
    /// <param name="ScenarioIds">Scénarios pris en compte (ceux demandés, visibles et existants).</param>
    public sealed record Forecast(
        PeriodCalculator Calculator,
        Period Period,
        IReadOnlyList<Guid> AccountIds,
        bool IncludesIncome,
        ForecastStrategy Strategy,
        int AverageMonths,
        IReadOnlyList<Guid> ScenarioIds,
        ForecastResult Result);

    /// <param name="horizon">Nombre de périodes prévues, la période en cours comprise (une valeur de <see cref="Horizons"/>).</param>
    /// <param name="scenarioIds">Scénarios à ajouter dans une série à part (F5) ; aucun par défaut.</param>
    public async Task<Forecast> ComputeAsync(CancellationToken ct, int horizon = 1, IReadOnlyCollection<Guid>? scenarioIds = null)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = PeriodCalculator.From(settings);
        var today = Balances.Today(clock);
        var period = calculator.For(today);
        var periods = new List<Period> { period };
        while (periods.Count < horizon)
        {
            periods.Add(calculator.Next(periods[^1]));
        }
        var horizonEnd = periods[^1].End;

        // Comptes de la prévision : ceux marqués « inclus dans la prévision » (par défaut, les comptes courants).
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IncludeInForecast && !a.IsArchived).ToListAsync(ct);
        var accountIds = accounts.Select(a => a.Id).ToHashSet();

        var before = await db.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.Date < period.Start)
            .SumAsync(t => t.Amount, ct);
        var opening = before + accounts.Where(a => a.InitialDate <= period.Start).Sum(a => a.InitialBalance);

        var lines = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { t.AccountId, t.Date, l.Amount, l.CategoryId })
            .Where(x => accountIds.Contains(x.AccountId) && x.Date >= period.Start && x.Date <= horizonEnd)
            .ToListAsync(ct);
        var transactions = lines.Select(l => new ForecastMovement(l.Date, l.Amount, l.CategoryId))
            // Un compte ouvert pendant l'horizon apporte son solde initial à sa date d'ouverture.
            .Concat(accounts.Where(a => a.InitialDate > period.Start && a.InitialDate <= horizonEnd)
                .Select(a => new ForecastMovement(a.InitialDate, a.InitialBalance, null)))
            .ToList();

        var scheduled = await ScheduledAsync(periods, accountIds, ct);

        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var averages = settings.ForecastStrategy == ForecastStrategy.Average
            ? await AveragesAsync(categories, calculator, period, settings.ForecastAverageMonths, ct)
            : null;

        var book = averages is null ? await BudgetBook.LoadAsync(db, settings, schedule, ct) : null;
        var budgets = new List<List<ForecastBudget>>();
        foreach (var p in periods)
        {
            budgets.Add(book is not null ? await BudgetRemaindersAsync(book, p, today, ct) : await AverageRemaindersAsync(categories, averages!, p, ct));
        }

        var scenarios = scenarioIds is { Count: > 0 } ? await ScenarioMovementsAsync(scenarioIds, accountIds, today, horizonEnd, ct) : null;

        var result = ForecastEngine.Compute(new ForecastInput(
            period,
            today,
            opening,
            [.. transactions.Where(t => t.Date <= period.End)],
            scheduled[0].Movements,
            scheduled[0].ByCategory,
            [.. categories.Select(c => new ForecastCategory(c.Id, c.ParentId, c.Kind))],
            budgets[0],
            settings.ForecastIncludesIncome,
            accounts.Sum(a => a.OverdraftThreshold),
            settings.SafetyThreshold,
            [.. periods.Skip(1).Select((p, i) => new ForecastFuturePeriod(
                p,
                [.. transactions.Where(t => p.Contains(t.Date))],
                scheduled[i + 1].Movements,
                scheduled[i + 1].ByCategory,
                budgets[i + 1]))],
            scenarios?.Movements));
        return new Forecast(
            calculator, period, [.. accountIds], settings.ForecastIncludesIncome, settings.ForecastStrategy, settings.ForecastAverageMonths,
            scenarios?.Ids ?? [], result);
    }

    /// <summary>
    /// Budget restant (R3) : budget qui sortira des comptes (provisions D11 et reste reporté D6 compris) − réalisé,
    /// par catégorie principale.
    /// </summary>
    private static async Task<List<ForecastBudget>> BudgetRemaindersAsync(BudgetBook book, Period period, DateOnly today, CancellationToken ct)
    {
        var (categories, _, _, budget, _, _) = await book.ComputeAsync(period, today, ct);
        // Les parts réservées des provisions restent sur les comptes : seul le montant à échéance en sort.
        return [.. categories
            .Where(c => c.ParentId is null && budget.Categories[c.Id].CashBudgeted > 0)
            .Select(c => new ForecastBudget(c.Id, budget.Categories[c.Id].CashBudgeted - budget.Categories[c.Id].Actual))];
    }

    /// <summary>
    /// Stratégie par la moyenne (F7) : réalisé moyen des <paramref name="months"/> périodes clôturées qui précèdent la
    /// période en cours, par catégorie principale (sous-catégories comprises), positif pour une dépense comme pour
    /// un revenu. Une moyenne nulle ou négative est écartée. Les provisions n'y ont pas de part propre : leurs
    /// dépenses passées sont déjà dans le réalisé.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> AveragesAsync(
        List<Domain.Categories.Category> categories, PeriodCalculator calculator, Period current, int months, CancellationToken ct)
    {
        var last = calculator.Previous(current);
        var first = last;
        for (var i = 1; i < months; i++)
        {
            first = calculator.Previous(first);
        }
        var (sums, _) = await BudgetEndpoints.ActualSumsAsync(db, first.Start, last.End, ct);
        return ByMainCategory(categories, sums)
            .Select(a => (a.Key, Average: Math.Round(a.Value / months, 2, MidpointRounding.AwayFromZero)))
            .Where(a => a.Average > 0)
            .ToDictionary(a => a.Key, a => a.Average);
    }

    /// <summary>Reste de la moyenne (F7) : moyenne − réalisé déjà saisi dans la période, par catégorie principale.</summary>
    private async Task<List<ForecastBudget>> AverageRemaindersAsync(
        List<Domain.Categories.Category> categories, Dictionary<Guid, decimal> averages, Period period, CancellationToken ct)
    {
        var (sums, _) = await BudgetEndpoints.ActualSumsAsync(db, period.Start, period.End, ct);
        var actual = ByMainCategory(categories, sums);
        return [.. averages.Select(a => new ForecastBudget(a.Key, a.Value - actual.GetValueOrDefault(a.Key)))];
    }

    /// <summary>Sommes signées par catégorie → montants orientés (positifs pour une dépense ou un revenu) par catégorie principale.</summary>
    private static Dictionary<Guid, decimal> ByMainCategory(List<Domain.Categories.Category> categories, Dictionary<Guid, decimal> sums)
    {
        var byId = categories.ToDictionary(c => c.Id);
        var result = new Dictionary<Guid, decimal>();
        foreach (var (id, sum) in sums)
        {
            if (!byId.TryGetValue(id, out var category))
            {
                continue;
            }
            var main = category.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : category;
            var sign = main.Kind == CategoryKind.Income ? 1m : -1m;
            result[main.Id] = result.GetValueOrDefault(main.Id) + sign * sum;
        }
        return result;
    }

    private sealed record ScheduledInPeriod(List<ForecastMovement> Movements, Dictionary<Guid, decimal> ByCategory);

    /// <summary>
    /// Échéances restantes jusqu'à la fin de l'horizon, réparties par période : leur effet sur les comptes de la
    /// prévision, et leur somme par catégorie sur tous les comptes (un virement ne compte, en sortie, que par sa
    /// catégorie d'épargne). Une échéance en retard appartient à la période en cours.
    /// </summary>
    private async Task<List<ScheduledInPeriod>> ScheduledAsync(List<Period> periods, HashSet<Guid> accountIds, CancellationToken ct)
    {
        var horizonEnd = periods[^1].End;
        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.StartDate <= horizonEnd)
            .ToListAsync(ct);
        var result = periods.Select(_ => new ScheduledInPeriod([], [])).ToList();
        foreach (var recurrence in recurrences)
        {
            foreach (var occurrence in RecurrenceSchedule.Remaining(recurrence.Rule, recurrence.GenerateFrom, recurrence.States, horizonEnd))
            {
                var index = occurrence.Date <= periods[0].End ? 0 : periods.FindIndex(p => p.Contains(occurrence.Date));
                if (index < 0)
                {
                    continue;
                }
                var target = result[index];
                var outflow = recurrence.ToAccountId is null ? recurrence.Amount : -recurrence.Amount;
                if (recurrence.CategoryId is { } categoryId)
                {
                    target.ByCategory[categoryId] = target.ByCategory.GetValueOrDefault(categoryId) + outflow;
                }
                if (accountIds.Contains(recurrence.AccountId))
                {
                    target.Movements.Add(new ForecastMovement(occurrence.Date, outflow, recurrence.CategoryId));
                }
                if (recurrence.ToAccountId is { } toAccountId && accountIds.Contains(toAccountId))
                {
                    target.Movements.Add(new ForecastMovement(occurrence.Date, recurrence.Amount, null));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Mouvements fictifs des scénarios demandés (F5), sur les comptes de la prévision, d'aujourd'hui à la fin de
    /// l'horizon. Les événements d'un compte privé d'un autre membre sont invisibles (R8) : ils ne comptent pas.
    /// </summary>
    /// <returns>Null si aucun des scénarios demandés n'existe.</returns>
    private async Task<(List<Guid> Ids, List<ForecastMovement> Movements)?> ScenarioMovementsAsync(
        IReadOnlyCollection<Guid> scenarioIds, HashSet<Guid> accountIds, DateOnly today, DateOnly horizonEnd, CancellationToken ct)
    {
        var ids = await db.Scenarios.AsNoTracking().Where(s => scenarioIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
        if (ids.Count == 0)
        {
            return null;
        }
        var events = await db.ScenarioEvents.AsNoTracking()
            .Where(e => ids.Contains(e.ScenarioId) && accountIds.Contains(e.AccountId))
            .ToListAsync(ct);
        var movements = events
            .SelectMany(e => e.DatesBetween(today, horizonEnd).Select(d => new ForecastMovement(d, e.Amount, e.CategoryId)))
            .ToList();
        return (ids, movements);
    }
}
