using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Budgets;

/// <summary>« À affecter » du mode enveloppes (D7) pour une période.</summary>
/// <param name="Since">Période d'activation du mode (clé AAAA-MM).</param>
/// <param name="Income">Revenus reçus dans la période.</param>
/// <param name="Assigned">Montants affectés dans la période : dépenses et épargne budgétées, hors reste reporté.</param>
/// <param name="ToAssign">
/// Cumul depuis l'activation de (revenus reçus − montants affectés), période comprise. Négatif : on a affecté plus
/// que ce qu'on a reçu.
/// </param>
public sealed record EnvelopeSummary(string Since, decimal Income, decimal Assigned, decimal ToAssign);

/// <summary>Budget calculé d'une période, avec ce qui a servi au calcul.</summary>
/// <param name="Overrides">Budgets propres à la période (D4).</param>
/// <param name="Uncategorized">Somme signée des opérations sans catégorie (hors virements).</param>
/// <param name="Envelope">« À affecter » (D7) ; null hors mode enveloppes ou avant son activation.</param>
public sealed record BudgetComputation(
    List<Category> Categories,
    Dictionary<Guid, CategoryBudget> Budgets,
    Dictionary<Guid, PeriodBudget> Overrides,
    BudgetResult Result,
    decimal Uncategorized,
    EnvelopeSummary? Envelope);

/// <summary>
/// Données du budget chargées une fois (catégories, budgets, provisions), pour calculer une période et, pour le
/// report du reste (D6) et « à affecter » (D7), les périodes qui la précèdent.
/// </summary>
/// <remarks>
/// Report : le reste d'une période (budget propre − réalisé propre, provisions comprises) passe sur la suivante,
/// qu'il soit positif ou négatif. Reporté de proche en proche, il vaut la somme des restes « de base » (sans
/// report) depuis l'activation. Une période à venir n'en reçoit pas : la prévision suppose déjà que le reste de la
/// période en cours sera dépensé, le reporter le compterait deux fois.
/// </remarks>
internal sealed class BudgetBook
{
    /// <summary>Garde-fou : nombre maximal de périodes remontées pour le report.</summary>
    public const int MaxCarryPeriods = 600;

    private readonly AppDbContext db;
    private readonly HouseholdSettings settings;
    private readonly IBudgetScheduleSource schedule;
    private readonly List<Category> categories;
    private readonly Dictionary<Guid, CategoryBudget> defaults;
    private readonly ILookup<string, PeriodBudget> overrides;
    private readonly List<Provision> provisions;

    private BudgetBook(
        AppDbContext db, HouseholdSettings settings, IBudgetScheduleSource schedule, List<Category> categories,
        Dictionary<Guid, CategoryBudget> defaults, ILookup<string, PeriodBudget> overrides, List<Provision> provisions)
    {
        this.db = db;
        this.settings = settings;
        this.schedule = schedule;
        this.categories = categories;
        this.defaults = defaults;
        this.overrides = overrides;
        this.provisions = provisions;
        Calculator = PeriodCalculator.From(settings);
    }

    public PeriodCalculator Calculator { get; }

    public static async Task<BudgetBook> LoadAsync(
        AppDbContext db, HouseholdSettings settings, IBudgetScheduleSource schedule, CancellationToken ct) =>
        new(db, settings, schedule,
            await db.Categories.AsNoTracking().ToListAsync(ct),
            await db.CategoryBudgets.AsNoTracking().ToDictionaryAsync(b => b.CategoryId, ct),
            (await db.PeriodBudgets.AsNoTracking().ToListAsync(ct)).ToLookup(b => b.PeriodKey),
            await db.Provisions.AsNoTracking().ToListAsync(ct));

    /// <summary>Seules les dépenses (fixes et variables) reportent leur reste (D6).</summary>
    public static bool CanCarryOver(Category c) => c.Kind is CategoryKind.Fixed or CategoryKind.Variable;

    /// <summary>Budget de la période, avec le reste reporté et « à affecter ».</summary>
    public async Task<BudgetComputation> ComputeAsync(Period period, DateOnly today, CancellationToken ct)
    {
        var envelopeSince = settings.EnvelopeSince;
        var since = categories
            .Where(c => !c.IsArchived && CanCarryOver(c))
            .Select(c => (c.Id, Since: Earliest(c.CarryOverSince, envelopeSince)))
            .Where(c => c.Since is not null)
            .ToDictionary(c => c.Id, c => c.Since!);

        // Première période à parcourir : la plus ancienne activation, report ou enveloppes.
        var first = since.Values.Append(envelopeSince).Where(k => k is not null).Min(StringComparer.Ordinal);
        var carried = new Dictionary<Guid, decimal>();
        var toAssign = 0m;
        if (first is not null && Calculator.TryParse(first, out var k))
        {
            for (var n = 0; Before(k, period) && n < MaxCarryPeriods; n++, k = Calculator.Next(k))
            {
                var (result, _) = await BaseAsync(k, null, ct);
                foreach (var (id, from) in since)
                {
                    if (!Before(k, from))
                    {
                        var r = result.Categories[id];
                        carried[id] = carried.GetValueOrDefault(id) + r.OwnBudgeted - r.OwnActual;
                    }
                }
                if (envelopeSince is not null && !Before(k, envelopeSince))
                {
                    toAssign += Unassigned(result);
                }
            }
        }

        // Le report n'est connu qu'une fois la période précédente close.
        var current = await BaseAsync(period, period.Start <= today ? carried : null, ct);
        EnvelopeSummary? envelope = null;
        if (envelopeSince is not null && !Before(period, envelopeSince))
        {
            var r = current.Result;
            envelope = new EnvelopeSummary(envelopeSince, r.Actual.Income, Assigned(r), toAssign + Unassigned(r));
        }
        return new BudgetComputation(
            categories, defaults, overrides[period.Key].ToDictionary(b => b.CategoryId), current.Result, current.Uncategorized, envelope);
    }

    /// <summary>Budget appliqué d'une catégorie pour une période : le sien, sinon celui par défaut.</summary>
    public BudgetCategory Definition(Category c, Period period) =>
        BudgetEndpoints.Definition(c, defaults.GetValueOrDefault(c.Id), overrides[period.Key].FirstOrDefault(b => b.CategoryId == c.Id));

    private async Task<(BudgetResult Result, decimal Uncategorized)> BaseAsync(
        Period period, Dictionary<Guid, decimal>? carried, CancellationToken ct)
    {
        var (actualSums, uncategorized) = await BudgetEndpoints.ActualSumsAsync(db, period.Start, period.End, ct);
        var result = BudgetCalculator.Compute(new BudgetInput(
            [.. categories.Select(c => Definition(c, period))],
            actualSums,
            await schedule.GetScheduledSumsAsync(period, ct),
            new BudgetThresholds(settings.BudgetWarningPercent, settings.BudgetAlertPercent),
            BudgetEndpoints.Provisions(provisions, Calculator, period),
            carried));
        return (result, uncategorized);
    }

    private static decimal Assigned(BudgetResult r) => r.Budgeted.Fixed + r.Budgeted.Variable + r.Budgeted.Savings;

    private static decimal Unassigned(BudgetResult r) => r.Actual.Income - Assigned(r);

    private static bool Before(Period period, string key) => string.CompareOrdinal(period.Key, key) < 0;

    private static bool Before(Period a, Period b) => string.CompareOrdinal(a.Key, b.Key) < 0;

    private static string? Earliest(string? a, string? b) =>
        a is null ? b : b is null ? a : string.CompareOrdinal(a, b) <= 0 ? a : b;
}
