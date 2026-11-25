using HomeBudget.Api.Domain.Forecasts;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Forecasts;

/// <summary>Rassemble les données de la période en cours et appelle <see cref="ForecastEngine"/>.</summary>
public sealed class ForecastService(AppDbContext db, TimeProvider clock, IBudgetScheduleSource schedule)
{
    public sealed record Forecast(PeriodCalculator Calculator, Period Period, IReadOnlyList<Guid> AccountIds, bool IncludesIncome, ForecastResult Result);

    public async Task<Forecast> ComputeAsync(CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = PeriodCalculator.From(settings);
        var today = Balances.Today(clock);
        var period = calculator.For(today);

        // Comptes de la prévision : ceux marqués « inclus dans la prévision » (par défaut, les comptes courants).
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IncludeInForecast && !a.IsArchived).ToListAsync(ct);
        var accountIds = accounts.Select(a => a.Id).ToHashSet();

        var before = await db.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.Date < period.Start)
            .SumAsync(t => t.Amount, ct);
        var opening = before + accounts.Where(a => a.InitialDate <= period.Start).Sum(a => a.InitialBalance);

        var lines = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { t.AccountId, t.Date, l.Amount, l.CategoryId })
            .Where(x => accountIds.Contains(x.AccountId) && x.Date >= period.Start && x.Date <= period.End)
            .ToListAsync(ct);
        var transactions = lines.Select(l => new ForecastMovement(l.Date, l.Amount, l.CategoryId))
            // Un compte ouvert en cours de période apporte son solde initial à sa date d'ouverture.
            .Concat(accounts.Where(a => a.InitialDate > period.Start && a.InitialDate <= period.End)
                .Select(a => new ForecastMovement(a.InitialDate, a.InitialBalance, null)))
            .ToList();

        var (scheduled, scheduledByCategory) = await ScheduledAsync(period, accountIds, ct);

        var (categories, _, budget, _) = await BudgetEndpoints.ComputeAsync(db, settings, period, schedule, ct);
        var budgets = categories
            .Where(c => c.ParentId is null && budget.Categories[c.Id].Budgeted > 0)
            .Select(c => new ForecastBudget(c.Id, budget.Categories[c.Id].Remaining))
            .ToList();

        var result = ForecastEngine.Compute(new ForecastInput(
            period,
            today,
            opening,
            transactions,
            scheduled,
            scheduledByCategory,
            [.. categories.Select(c => new ForecastCategory(c.Id, c.ParentId, c.Kind))],
            budgets,
            settings.ForecastIncludesIncome,
            accounts.Sum(a => a.OverdraftThreshold),
            settings.SafetyThreshold));
        return new Forecast(calculator, period, [.. accountIds], settings.ForecastIncludesIncome, result);
    }

    /// <summary>
    /// Échéances restantes jusqu'à la fin de la période : leur effet sur les comptes de la prévision, et leur
    /// somme par catégorie sur tous les comptes (un virement ne compte, en sortie, que par sa catégorie d'épargne).
    /// </summary>
    private async Task<(List<ForecastMovement> Scheduled, Dictionary<Guid, decimal> ByCategory)> ScheduledAsync(
        Period period, HashSet<Guid> accountIds, CancellationToken ct)
    {
        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.StartDate <= period.End)
            .ToListAsync(ct);
        var scheduled = new List<ForecastMovement>();
        var byCategory = new Dictionary<Guid, decimal>();
        foreach (var recurrence in recurrences)
        {
            foreach (var occurrence in RecurrenceSchedule.Remaining(recurrence.Rule, recurrence.GenerateFrom, recurrence.States, period.End))
            {
                var outflow = recurrence.ToAccountId is null ? recurrence.Amount : -recurrence.Amount;
                if (recurrence.CategoryId is { } categoryId)
                {
                    byCategory[categoryId] = byCategory.GetValueOrDefault(categoryId) + outflow;
                }
                if (accountIds.Contains(recurrence.AccountId))
                {
                    scheduled.Add(new ForecastMovement(occurrence.Date, outflow, recurrence.CategoryId));
                }
                if (recurrence.ToAccountId is { } toAccountId && accountIds.Contains(toAccountId))
                {
                    scheduled.Add(new ForecastMovement(occurrence.Date, recurrence.Amount, null));
                }
            }
        }
        return (scheduled, byCategory);
    }
}
