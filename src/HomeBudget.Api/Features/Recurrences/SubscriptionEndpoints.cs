using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Recurrences;

/// <summary>Vue « Abonnements » (E10) : les récurrences marquées comme abonnement, toujours actives, et leur coût.</summary>
public static class SubscriptionEndpoints
{
    private const int HorizonYears = 10;

    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/recurrences/subscriptions", ListAsync).WithTags("Recurrences")
            .WithSummary("Abonnements en cours, avec leur coût mensuel et annuel, et les totaux.");
        return app;
    }

    /// <param name="Amount">Montant d'une échéance (négatif pour une dépense).</param>
    /// <param name="MonthlyCost">Coût mensuel moyen, en positif pour une dépense.</param>
    /// <param name="AnnualCost">Coût annuel, en positif pour une dépense.</param>
    public sealed record SubscriptionDto(
        Guid RecurrenceId,
        string Label,
        string? PayeeName,
        Guid? CategoryId,
        Guid AccountId,
        decimal Amount,
        FrequencyUnit Unit,
        int Interval,
        DateOnly? NextDate,
        decimal MonthlyCost,
        decimal AnnualCost);

    public sealed record SubscriptionsDto(decimal MonthlyTotal, decimal AnnualTotal, List<SubscriptionDto> Items);

    private static async Task<Ok<SubscriptionsDto>> ListAsync(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var today = Balances.Today(clock);
        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.IsSubscription && r.ToAccountId == null)
            .ToListAsync(ct);
        var payeeIds = recurrences.Select(r => r.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var items = recurrences
            .Select(r => (Recurrence: r, Next: RecurrenceSchedule.Remaining(r.Rule, r.GenerateFrom, r.States, today.AddYears(HorizonYears))))
            .Where(x => x.Next.Count > 0) // un abonnement terminé ne coûte plus rien
            .Select(x => new SubscriptionDto(
                x.Recurrence.Id,
                x.Recurrence.Label,
                x.Recurrence.PayeeId is { } p ? payees.GetValueOrDefault(p) : null,
                x.Recurrence.CategoryId,
                x.Recurrence.AccountId,
                x.Recurrence.Amount,
                x.Recurrence.Unit,
                x.Recurrence.Interval,
                x.Next[0].Date,
                SubscriptionCost.Monthly(-x.Recurrence.Amount, x.Recurrence.Unit, x.Recurrence.Interval),
                SubscriptionCost.Annual(-x.Recurrence.Amount, x.Recurrence.Unit, x.Recurrence.Interval)))
            .OrderByDescending(s => s.MonthlyCost).ThenBy(s => s.Label)
            .ToList();
        return TypedResults.Ok(new SubscriptionsDto(items.Sum(s => s.MonthlyCost), items.Sum(s => s.AnnualCost), items));
    }
}
