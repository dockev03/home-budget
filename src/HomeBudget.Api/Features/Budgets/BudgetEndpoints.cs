using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Budgets;

/// <summary>Budget par catégorie et par période, comparé au réel (D1, D2, D3, D8, D9).</summary>
public static class BudgetEndpoints
{
    private const decimal MaxAmount = 10_000_000m;

    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/budgets").WithTags("Budgets");

        group.MapGet("/{periodKey}", GetAsync)
            .Produces<BudgetDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Budget de la période (clé AAAA-MM) comparé au réel, par catégorie, avec le récapitulatif.");
        group.MapPut("/categories/{categoryId:guid}", SetAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<CategoryBudgetDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Définit le budget d'une catégorie. If-Match requis si un budget existe déjà (sa version).");
        group.MapDelete("/categories/{categoryId:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime le budget d'une catégorie (If-Match requis) : retour au mode par défaut.");

        return app;
    }

    public sealed record CategoryBudgetRequest(BudgetMode Mode, decimal Value);

    public sealed record CategoryBudgetDto(Guid CategoryId, BudgetMode Mode, decimal Value, int Version);

    public sealed record KindTotalsDto(decimal Income, decimal Fixed, decimal Variable, decimal Savings, decimal Remaining);

    /// <param name="Mode">Mode effectif (null : pas de budget).</param>
    /// <param name="IsDefaultMode">Mode appliqué par défaut (dépense fixe sans budget défini : Auto).</param>
    /// <param name="Value">Valeur saisie : montant ou pourcentage (null si aucun budget défini).</param>
    /// <param name="BudgetVersion">Version du budget défini, pour If-Match (null si aucun).</param>
    /// <param name="OwnBudgeted">Budget propre, hors sous-catégories.</param>
    /// <param name="Budgeted">Budget, y compris celui des sous-catégories.</param>
    /// <param name="Actual">Réalisé, y compris les sous-catégories : positif pour une dépense comme pour un revenu.</param>
    /// <param name="PercentUsed">Part du budget consommée, en % (null sans budget).</param>
    public sealed record BudgetLineDto(
        Guid CategoryId,
        Guid? ParentId,
        string Name,
        CategoryKind Kind,
        string? Color,
        string? Icon,
        bool IsArchived,
        BudgetMode? Mode,
        bool IsDefaultMode,
        decimal? Value,
        int? BudgetVersion,
        decimal OwnBudgeted,
        decimal Budgeted,
        decimal Actual,
        decimal Remaining,
        decimal? PercentUsed,
        BudgetStatus Status);

    /// <param name="IncomeBase">Revenus budgétés : la base des budgets en pourcentage.</param>
    /// <param name="Uncategorized">Somme signée des opérations sans catégorie (hors virements), hors récapitulatif.</param>
    public sealed record BudgetDto(
        PeriodEndpoints.PeriodDto Period,
        KindTotalsDto Budgeted,
        KindTotalsDto Actual,
        decimal IncomeBase,
        decimal Uncategorized,
        int WarningPercent,
        int AlertPercent,
        List<BudgetLineDto> Categories);

    private static async Task<IResult> GetAsync(
        string periodKey, AppDbContext db, TimeProvider clock, IBudgetScheduleSource schedule, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = Domain.Periods.PeriodCalculator.From(settings);
        if (!calculator.TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }

        var (categories, budgets, result, uncategorized) = await ComputeAsync(db, settings, period, schedule, ct);

        // Une catégorie archivée n'apparaît que si elle a encore un budget ou du réalisé sur la période.
        bool Visible(Category c) =>
            !c.IsArchived || result.Categories[c.Id] is { Budgeted: not 0 } or { Actual: not 0 };

        var lines = new List<BudgetLineDto>();
        var parents = categories.Where(c => c.ParentId is null).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name);
        foreach (var parent in parents.Where(Visible))
        {
            lines.Add(ToLine(parent, budgets.GetValueOrDefault(parent.Id), result.Categories[parent.Id]));
            lines.AddRange(categories.Where(c => c.ParentId == parent.Id && Visible(c))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => ToLine(c, budgets.GetValueOrDefault(c.Id), result.Categories[c.Id])));
        }

        return TypedResults.Ok(new BudgetDto(
            PeriodEndpoints.ToDto(calculator, period, clock),
            ToDto(result.Budgeted),
            ToDto(result.Actual),
            result.IncomeBase,
            uncategorized,
            settings.BudgetWarningPercent,
            settings.BudgetAlertPercent,
            lines));
    }

    /// <summary>Calcul du budget d'une période, partagé avec la prévision.</summary>
    /// <returns>Les catégories, les budgets définis, le résultat, et la somme des opérations sans catégorie.</returns>
    internal static async Task<(List<Category> Categories, Dictionary<Guid, CategoryBudget> Budgets, BudgetResult Result, decimal Uncategorized)>
        ComputeAsync(AppDbContext db, Domain.Settings.HouseholdSettings settings, Domain.Periods.Period period, IBudgetScheduleSource schedule, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var budgets = await db.CategoryBudgets.AsNoTracking().ToDictionaryAsync(b => b.CategoryId, ct);

        // Réalisé : lignes de la période regroupées en SQL. Les lignes de virement sans catégorie sont
        // exclues (R5) ; seule la ligne d'un virement vers l'épargne porte une catégorie.
        var sums = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Date, IsTransfer = t.TransferId != null })
            .Where(x => x.Date >= period.Start && x.Date <= period.End && !(x.CategoryId == null && x.IsTransfer))
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var actualSums = sums.Where(s => s.CategoryId is not null).ToDictionary(s => s.CategoryId!.Value, s => s.Sum);
        var uncategorized = sums.Where(s => s.CategoryId is null).Sum(s => s.Sum);

        var result = BudgetCalculator.Compute(new BudgetInput(
            categories.Select(c => new BudgetCategory(
                c.Id, c.ParentId, c.Kind, budgets.GetValueOrDefault(c.Id)?.Mode, budgets.GetValueOrDefault(c.Id)?.Value ?? 0m)).ToList(),
            actualSums,
            await schedule.GetScheduledSumsAsync(period, ct),
            new BudgetThresholds(settings.BudgetWarningPercent, settings.BudgetAlertPercent)));
        return (categories, budgets, result, uncategorized);
    }

    private static async Task<IResult> SetAsync(Guid categoryId, CategoryBudgetRequest request, HttpRequest http, AppDbContext db)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
        {
            return TypedResults.NotFound();
        }
        var budget = await db.CategoryBudgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId);
        if (budget is not null && Concurrency.Check(http, budget) is { } precondition)
        {
            return precondition;
        }
        if (Validate(category, request) is { } problem)
        {
            return problem;
        }

        if (budget is null)
        {
            budget = new CategoryBudget { CategoryId = categoryId };
            db.CategoryBudgets.Add(budget);
        }
        budget.Mode = request.Mode;
        budget.Value = request.Mode == BudgetMode.Auto ? 0m : request.Value;

        // Deux créations simultanées du même budget se heurtent à l'index unique : on le signale comme un conflit.
        try
        {
            if (!await db.TrySaveAsync())
            {
                return Concurrency.Conflict();
            }
        }
        catch (DbUpdateException)
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok(new CategoryBudgetDto(budget.CategoryId, budget.Mode, budget.Value, budget.Version));
    }

    private static async Task<IResult> DeleteAsync(Guid categoryId, HttpRequest http, AppDbContext db)
    {
        var budget = await db.CategoryBudgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId);
        if (budget is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, budget) is { } precondition)
        {
            return precondition;
        }
        db.CategoryBudgets.Remove(budget);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static IResult? Validate(Category category, CategoryBudgetRequest request)
    {
        string? error = request.Mode switch
        {
            _ when !Enum.IsDefined(request.Mode) => "Mode de budget inconnu.",
            BudgetMode.PercentOfIncome when category.Kind == CategoryKind.Income =>
                "Un revenu ne peut pas être budgété en pourcentage des revenus.",
            BudgetMode.PercentOfIncome when request.Value is < 0 or > 100 => "Le pourcentage va de 0 à 100.",
            BudgetMode.Amount when request.Value is < 0 or > MaxAmount => "Le montant doit être positif.",
            BudgetMode.Amount or BudgetMode.PercentOfIncome when request.Value != Math.Round(request.Value, 2) =>
                "Deux décimales au maximum.",
            _ => null,
        };
        return error is null ? null : TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [request.Mode == BudgetMode.PercentOfIncome && category.Kind == CategoryKind.Income ? "mode" : "value"] = [error],
        });
    }

    private static BudgetLineDto ToLine(Category c, CategoryBudget? budget, CategoryBudgetResult r) =>
        new(c.Id, c.ParentId, c.Name, c.Kind, c.Color, c.Icon, c.IsArchived,
            r.Mode, r.IsDefaultMode, budget?.Value, budget?.Version,
            r.OwnBudgeted, r.Budgeted, r.Actual, r.Remaining, r.PercentUsed, r.Status);

    private static KindTotalsDto ToDto(KindTotals t) => new(t.Income, t.Fixed, t.Variable, t.Savings, t.Remaining);
}
