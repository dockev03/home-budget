using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Recurrences;

/// <summary>
/// Budget Auto (D2) : somme signée des échéances prévues de la période, par catégorie. Les échéances sautées
/// sont exclues ; un virement ne compte que s'il porte une catégorie (d'épargne), du côté de la sortie.
/// </summary>
public sealed class RecurrenceBudgetSource(AppDbContext db) : IBudgetScheduleSource
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetScheduledSumsAsync(Period period, CancellationToken ct)
    {
        var recurrences = await db.Recurrences.AsNoTracking()
            .Where(r => r.CategoryId != null && r.StartDate <= period.End)
            .Include(r => r.Occurrences.Where(o => o.Status == OccurrenceStatus.Skipped))
            .ToListAsync(ct);

        var sums = new Dictionary<Guid, decimal>();
        foreach (var recurrence in recurrences)
        {
            var count = RecurrenceSchedule.Planned(recurrence.Rule, recurrence.States, period.Start, period.End).Count;
            var amount = recurrence.ToAccountId is null ? recurrence.Amount : -recurrence.Amount;
            sums[recurrence.CategoryId!.Value] = sums.GetValueOrDefault(recurrence.CategoryId.Value) + count * amount;
        }
        return sums;
    }
}
