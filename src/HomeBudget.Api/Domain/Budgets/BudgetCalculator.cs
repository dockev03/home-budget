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

/// <summary>Provisions d'une catégorie pour une période (D11), montants positifs.</summary>
/// <param name="Shares">Parts réservées par toutes les provisions de la période, échéances comprises.</param>
/// <param name="DueShares">Parts des provisions dont c'est la période d'échéance.</param>
/// <param name="DueAmounts">Montants entiers des provisions dont c'est la période d'échéance.</param>
public readonly record struct ProvisionAmounts(decimal Shares, decimal DueShares, decimal DueAmounts)
{
    /// <summary>Ce dont la catégorie dispose : les parts, et le montant entier à l'échéance au lieu de sa part.</summary>
    public decimal Available => Shares - DueShares + DueAmounts;
}

/// <summary>Entrées du calcul pour une période.</summary>
/// <param name="ActualSums">Somme signée des lignes d'opérations de la période, par catégorie.</param>
/// <param name="ScheduledSums">Somme signée des échéances de récurrence de la période, par catégorie.</param>
/// <param name="Provisions">Provisions de la période, par catégorie (D11).</param>
/// <param name="CarriedOver">Reste reporté des périodes précédentes, par catégorie (D6), positif ou négatif.</param>
public sealed record BudgetInput(
    IReadOnlyList<BudgetCategory> Categories,
    IReadOnlyDictionary<Guid, decimal> ActualSums,
    IReadOnlyDictionary<Guid, decimal> ScheduledSums,
    BudgetThresholds Thresholds,
    IReadOnlyDictionary<Guid, ProvisionAmounts>? Provisions = null,
    IReadOnlyDictionary<Guid, decimal>? CarriedOver = null);

/// <param name="Mode">Mode effectif : celui défini, ou Auto par défaut pour une dépense fixe (D3).</param>
/// <param name="OwnBudgeted">Budget propre, provisions et reste reporté compris (ce dont la catégorie dispose).</param>
/// <param name="Budgeted">Budget, cumulé avec celui des sous-catégories pour une catégorie parente.</param>
/// <param name="Actual">Réalisé (positif pour une dépense ou un revenu), cumulé de la même façon.</param>
/// <param name="OwnProvisioned">Part du budget propre qui vient des provisions (D11).</param>
/// <param name="CashBudgeted">
/// Budget qui sortira réellement des comptes, cumulé : sans les parts réservées des provisions, mais avec le
/// montant entier d'une provision à échéance et le reste reporté. C'est lui que la prévision utilise (R3).
/// </param>
/// <param name="OwnActual">Réalisé propre, hors sous-catégories.</param>
/// <param name="OwnCarriedOver">Part du budget propre qui vient du reste reporté (D6).</param>
public sealed record CategoryBudgetResult(
    Guid CategoryId,
    BudgetMode? Mode,
    bool IsDefaultMode,
    decimal OwnBudgeted,
    decimal Budgeted,
    decimal Actual,
    decimal Remaining,
    decimal? PercentUsed,
    BudgetStatus Status,
    decimal OwnProvisioned = 0,
    decimal CashBudgeted = 0,
    decimal OwnActual = 0,
    decimal OwnCarriedOver = 0);

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
/// Provisions (D11) : chaque période réserve sa part dans le récapitulatif (D8) ; la période de l'échéance
/// dispose en plus du montant déjà réservé, ce qui élargit la jauge de la catégorie sans peser une seconde
/// fois sur le récapitulatif.
/// Reste reporté (D6) : il élargit (ou réduit) la jauge de la catégorie, mais pas le récapitulatif, qui ne
/// retient que ce que les revenus de la période financent.
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

        var provisions = input.Provisions ?? new Dictionary<Guid, ProvisionAmounts>();
        ProvisionAmounts ProvisionOf(Guid id) => provisions.GetValueOrDefault(id);
        var carried = input.CarriedOver ?? new Dictionary<Guid, decimal>();
        decimal CarryOf(Guid id) => carried.GetValueOrDefault(id);
        decimal Available(Guid id) => own[id].Budgeted + ProvisionOf(id).Available + CarryOf(id);
        decimal Cash(Guid id) => own[id].Budgeted + ProvisionOf(id).DueAmounts + CarryOf(id);

        var results = new Dictionary<Guid, CategoryBudgetResult>();
        foreach (var category in input.Categories)
        {
            var childIds = input.Categories.Where(c => c.ParentId == category.Id).Select(c => c.Id).ToList();
            var mine = own[category.Id];
            var ownBudgeted = Available(category.Id);
            var budgeted = ownBudgeted + childIds.Sum(Available);
            var actual = mine.Actual + childIds.Sum(c => own[c].Actual);
            // Le statut se décide sur le ratio exact : 79,99 % ne doit pas devenir 80 % par l'arrondi d'affichage.
            decimal? percent = budgeted > 0 ? actual / budgeted * 100m : null;
            results[category.Id] = new CategoryBudgetResult(
                category.Id, mine.Mode, mine.IsDefault, ownBudgeted, budgeted, actual, budgeted - actual,
                percent is { } p ? Math.Round(p, 1, MidpointRounding.AwayFromZero) : null,
                Status(category.Kind, actual, percent, input.Thresholds),
                ProvisionOf(category.Id).Available,
                Cash(category.Id) + childIds.Sum(Cash),
                mine.Actual,
                CarryOf(category.Id));
        }

        return new BudgetResult(
            results,
            Totals(input.Categories, c => own[c].Budgeted + ProvisionOf(c).Shares),
            Totals(input.Categories, c => own[c].Actual),
            incomeBase);
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
