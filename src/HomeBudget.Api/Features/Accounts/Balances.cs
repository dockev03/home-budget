using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>
/// Soldes des comptes : jusqu'à aujourd'hui (solde réel) et au total (y compris futur, B4). Un compte valorisé (A7)
/// part de sa dernière valorisation et n'ajoute que les opérations datées après elle.
/// </summary>
public static class Balances
{
    public readonly record struct AccountBalance(decimal Current, decimal Future);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public static async Task<IReadOnlyDictionary<Guid, AccountBalance>> ComputeAsync(
        AppDbContext db, TimeProvider clock, IEnumerable<Account> accounts, CancellationToken ct = default)
    {
        var list = accounts.ToList();
        var ids = list.Select(a => a.Id).ToList();
        var today = Today(clock);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => new { t.AccountId, Past = t.Date <= today })
            .Select(g => new { g.Key.AccountId, g.Key.Past, Sum = g.Sum(t => t.Amount) })
            .ToListAsync(ct);
        var sums = rows.GroupBy(r => r.AccountId).ToDictionary(
            g => g.Key,
            g => (UntilToday: g.Where(r => r.Past).Sum(r => r.Sum), Total: g.Sum(r => r.Sum)));

        // Dernière valorisation de chaque compte, au plus tard aujourd'hui.
        var valuations = (await db.AccountValuations.AsNoTracking()
                .Where(v => ids.Contains(v.AccountId) && v.Date <= today)
                .Select(v => new { v.AccountId, v.Date, v.Value })
                .ToListAsync(ct))
            .GroupBy(v => v.AccountId)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Date)!);

        var result = new Dictionary<Guid, AccountBalance>();
        foreach (var account in list)
        {
            var (untilToday, total) = sums.GetValueOrDefault(account.Id);
            if (valuations.TryGetValue(account.Id, out var valuation))
            {
                var covered = await db.Transactions.AsNoTracking()
                    .Where(t => t.AccountId == account.Id && t.Date <= valuation.Date)
                    .SumAsync(t => t.Amount, ct);
                result[account.Id] = new AccountBalance(valuation.Value + untilToday - covered, valuation.Value + total - covered);
            }
            else
            {
                result[account.Id] = new AccountBalance(account.InitialBalance + untilToday, account.InitialBalance + total);
            }
        }
        return result;
    }
}
