using HomeBudget.Api.Domain.Periods;

namespace HomeBudget.Api.Features.Budgets;

/// <summary>
/// Montants des échéances de récurrence d'une période, par catégorie (somme signée), qui alimentent
/// le mode de budget Auto (D2). Implémentée par <c>RecurrenceBudgetSource</c>.
/// </summary>
public interface IBudgetScheduleSource
{
    Task<IReadOnlyDictionary<Guid, decimal>> GetScheduledSumsAsync(Period period, CancellationToken ct);
}
