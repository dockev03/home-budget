using HomeBudget.Api.Domain.Periods;

namespace HomeBudget.Api.Features.Budgets;

/// <summary>
/// Montants des échéances de récurrence d'une période, par catégorie (somme signée), qui alimentent
/// le mode de budget Auto (D2).
/// </summary>
public interface IBudgetScheduleSource
{
    Task<IReadOnlyDictionary<Guid, decimal>> GetScheduledSumsAsync(Period period, CancellationToken ct);
}

/// <summary>
/// Source vide, en attendant les récurrences (section E du cahier des charges) : un budget Auto vaut 0 €.
/// </summary>
public sealed class NoScheduleSource : IBudgetScheduleSource
{
    public Task<IReadOnlyDictionary<Guid, decimal>> GetScheduledSumsAsync(Period period, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
}
