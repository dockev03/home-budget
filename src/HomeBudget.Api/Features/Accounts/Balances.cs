using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

/// <summary>Mouvements cumulés par compte : jusqu'à aujourd'hui (solde réel) et au total (y compris futur, B4).</summary>
public static class Balances
{
    public readonly record struct AccountBalance(decimal UntilToday, decimal Total);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public static async Task<IReadOnlyDictionary<Guid, AccountBalance>> ComputeAsync(
        AppDbContext db, TimeProvider clock, IEnumerable<Guid> accountIds)
    {
        var ids = accountIds.ToList();
        var today = Today(clock);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => new { t.AccountId, Past = t.Date <= today })
            .Select(g => new { g.Key.AccountId, g.Key.Past, Sum = g.Sum(t => t.Amount) })
            .ToListAsync();
        return rows.GroupBy(r => r.AccountId).ToDictionary(
            g => g.Key,
            g => new AccountBalance(g.Where(r => r.Past).Sum(r => r.Sum), g.Sum(r => r.Sum)));
    }
}
