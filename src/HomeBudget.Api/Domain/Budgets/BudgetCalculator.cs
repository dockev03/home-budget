using HomeBudget.Api.Domain.Categories;

namespace HomeBudget.Api.Domain.Budgets;

public enum BudgetStatus
{
    /// <summary>Pas de jauge : revenu, épargne, ou dépense sans budget ni réalisé.</summary>
    None,
    Ok,
    Warning,
    Exceeded,
}

/// <summary>Catégorie et son budget défini (null si aucun).</summary>
public sealed record BudgetCategory(Guid Id, Guid? ParentId, CategoryKind Kind, BudgetMode? Mode, decimal Value);

public sealed record BudgetThresholds(int WarningPercent, int AlertPercent);

/// <summary>Entrées du calcul pour une période.</summary>
/// <param name="ActualSums">Somme signée des lignes d'opérations de la période, par catégorie.</param>
/// <param name="ScheduledSums">Somme signée des échéances de récurrence de la période, par catégorie.</param>
public sealed record BudgetInput(
    IReadOnlyList<BudgetCategory> Categories,
    IReadOnlyDictionary<Guid, decimal> ActualSums,
    IReadOnlyDictionary<Guid, decimal> ScheduledSums,
    BudgetThresholds Thresholds);

/// <param name="Mode">Mode effectif : celui défini, ou Auto par défaut pour une dépense fixe (D3).</param>
/// <param name="Budgeted">Budget, cumulé avec celui des sous-catégories pour une catégorie parente.</param>
/// <param name="Actual">Réalisé (positif pour une dépense ou un revenu), cumulé de la même façon.</param>
public sealed record CategoryBudgetResult(
    Guid CategoryId,
    BudgetMode? Mode,
    bool IsDefaultMode,
    decimal OwnBudgeted,
    decimal Budgeted,
    decimal Actual,
    decimal Remaining,
    decimal? PercentUsed,
    BudgetStatus Status);

public sealed record KindTotals(decimal Income, decimal Fixed, decimal Variable, decimal Savings)
{
    /// <summary>Restant = Revenus − Fixes − Variables − Épargne (D8).</summary>
    public decimal Remaining => Income - Fixed - Variable - Savings;
}

public sealed record BudgetResult(
    IReadOnlyDictionary<Guid, CategoryBudgetResult> Categories,
    KindTotals Budgeted,
    KindTotals Actual,
    decimal IncomeBase);

/// <summary>
/// Calcul du budget d'une période (D1, D2, D3, D8, D9), sans accès à la base.
/// </summary>
/// <remarks>
/// Ordre : les revenus (montant ou Auto) donnent la base des pourcentages, puis les autres catégories.
/// Le réalisé d'une dépense vaut −Σ(montants) (R2) : un remboursement la diminue.
/// </remarks>
public static class BudgetCalculator
{
    public static BudgetResult Compute(BudgetInput input)
    {
        var own = new Dictionary<Guid, (BudgetMode? Mode, bool IsDefault, decimal Budgeted, decimal Actual)>();

        foreach (var category in input.Categories)
        {
            var (mode, isDefault) = EffectiveMode(category);
            var sign = category.Kind == CategoryKind.Income ? 1m : -1m;
            var actual = sign * input.ActualSums.GetValueOrDefault(category.Id);
            var budgeted = mode switch
            {
                BudgetMode.Amount => category.Value,
                BudgetMode.Auto => sign * input.ScheduledSums.GetValueOrDefault(category.Id),
                _ => 0m, // pourcentages calculés une fois la base connue
            };
            own[category.Id] = (mode, isDefault, budgeted, actual);
        }

        var incomeBase = input.Categories.Where(c => c.Kind == CategoryKind.Income).Sum(c => own[c.Id].Budgeted);
        foreach (var category in input.Categories.Where(c => own[c.Id].Mode == BudgetMode.PercentOfIncome))
        {
            own[category.Id] = own[category.Id] with
            {
                Budgeted = Math.Round(incomeBase * category.Value / 100m, 2, MidpointRounding.AwayFromZero),
            };
        }

        var results = new Dictionary<Guid, CategoryBudgetResult>();
        foreach (var category in input.Categories)
        {
            var children = input.Categories.Where(c => c.ParentId == category.Id).Select(c => own[c.Id]).ToList();
            var mine = own[category.Id];
            var budgeted = mine.Budgeted + children.Sum(c => c.Budgeted);
            var actual = mine.Actual + children.Sum(c => c.Actual);
            // Le statut se décide sur le ratio exact : 79,99 % ne doit pas devenir 80 % par l'arrondi d'affichage.
            decimal? percent = budgeted > 0 ? actual / budgeted * 100m : null;
            results[category.Id] = new CategoryBudgetResult(
                category.Id, mine.Mode, mine.IsDefault, mine.Budgeted, budgeted, actual, budgeted - actual,
                percent is { } p ? Math.Round(p, 1, MidpointRounding.AwayFromZero) : null,
                Status(category.Kind, actual, percent, input.Thresholds));
        }

        return new BudgetResult(results, Totals(input.Categories, c => own[c].Budgeted), Totals(input.Categories, c => own[c].Actual), incomeBase);
    }

    private static (BudgetMode? Mode, bool IsDefault) EffectiveMode(BudgetCategory category) =>
        category.Mode is { } mode ? (mode, false)
        : category.Kind == CategoryKind.Fixed ? (BudgetMode.Auto, true)
        : (null, false);

    /// <summary>Jauge D9 : seulement pour les dépenses (fixes et variables).</summary>
    private static BudgetStatus Status(CategoryKind kind, decimal actual, decimal? percent, BudgetThresholds thresholds)
    {
        if (kind is not (CategoryKind.Fixed or CategoryKind.Variable))
        {
            return BudgetStatus.None;
        }
        if (percent is not { } used)
        {
            return actual > 0 ? BudgetStatus.Exceeded : BudgetStatus.None;
        }
        return used >= thresholds.AlertPercent ? BudgetStatus.Exceeded
            : used >= thresholds.WarningPercent ? BudgetStatus.Warning
            : BudgetStatus.Ok;
    }

    private static KindTotals Totals(IReadOnlyList<BudgetCategory> categories, Func<Guid, decimal> amount)
    {
        decimal Sum(CategoryKind kind) => categories.Where(c => c.Kind == kind).Sum(c => amount(c.Id));
        return new KindTotals(Sum(CategoryKind.Income), Sum(CategoryKind.Fixed), Sum(CategoryKind.Variable), Sum(CategoryKind.Savings));
    }
}
