using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Budgets;

/// <summary>Budget par catégorie et par période, comparé au réel (D1 à D9, D11), report du reste (D6) et enveloppes (D7).</summary>
public static class BudgetEndpoints
{
    private const decimal MaxAmount = 10_000_000m;

    /// <summary>Plus longue moyenne proposée pour copier un budget (D5).</summary>
    public const int MaxAverageMonths = 24;

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
        group.MapPut("/{periodKey}/categories/{categoryId:guid}", SetPeriodAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<PeriodBudgetDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Budget d'une catégorie pour cette seule période (D4). If-Match requis s'il existe déjà.");
        group.MapDelete("/{periodKey}/categories/{categoryId:guid}", DeletePeriodAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime le budget propre à la période (If-Match requis) : retour au budget par défaut.");
        group.MapPost("/{periodKey}/copy/preview", PreviewCopyAsync)
            .Produces<List<BudgetCopyProposalDto>>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Aperçu de la copie du budget (D5) : depuis la période précédente ou la moyenne du réalisé sur N mois.");
        group.MapPost("/{periodKey}/copy", CopyAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<BudgetCopyResultDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Applique la copie du budget aux catégories choisies, en budgets propres à la période (D4, D5).");
        group.MapPut("/categories/{categoryId:guid}/carry-over", SetCarryOverAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<CarryOverDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Active ou désactive le report du reste d'une dépense sur la période suivante (D6).");
        group.MapPost("/{periodKey}/moves", MoveAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<BudgetMoveDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Déplace un montant du budget d'une catégorie vers une autre, pour cette période (D7).");

        return app;
    }

    public enum BudgetCopySource
    {
        /// <summary>Budget de la période précédente (budgets propres compris).</summary>
        PreviousPeriod,

        /// <summary>Moyenne du réalisé des N périodes précédentes, en montant.</summary>
        Average,
    }

    /// <param name="Months">Nombre de périodes de la moyenne (1 à 24) ; ignoré pour la période précédente.</param>
    /// <param name="CategoryIds">Catégories à copier ; toutes celles proposées si absent.</param>
    public sealed record BudgetCopyRequest(BudgetCopySource Source, int? Months, IReadOnlyList<Guid>? CategoryIds);

    /// <param name="CurrentMode">Mode effectif actuel de la période (null : pas de budget).</param>
    /// <param name="CurrentBudgeted">Budget actuel de la catégorie seule, hors provisions.</param>
    /// <param name="ProposedMode">Mode proposé (null : pas de budget).</param>
    public sealed record BudgetCopyProposalDto(
        Guid CategoryId,
        Guid? ParentId,
        string Name,
        CategoryKind Kind,
        BudgetMode? CurrentMode,
        decimal? CurrentValue,
        decimal CurrentBudgeted,
        BudgetMode? ProposedMode,
        decimal? ProposedValue);

    /// <param name="Applied">Catégories dont le budget de la période a changé.</param>
    public sealed record BudgetCopyResultDto(int Applied);

    public sealed record PeriodBudgetDto(Guid CategoryId, string PeriodKey, BudgetMode Mode, decimal Value, int Version);

    /// <summary>Budget défini : par défaut ou propre à la période.</summary>
    public sealed record DefinedBudgetDto(BudgetMode Mode, decimal Value, int Version);

    public sealed record CategoryBudgetRequest(BudgetMode Mode, decimal Value);

    public sealed record CategoryBudgetDto(Guid CategoryId, BudgetMode Mode, decimal Value, int Version);

    /// <param name="Enabled">Report activé.</param>
    /// <param name="Since">Première période reportée (AAAA-MM) ; la période en cours si absente.</param>
    public sealed record CarryOverRequest(bool Enabled, string? Since);

    /// <param name="Since">Première période dont le reste est reporté (null : report désactivé).</param>
    public sealed record CarryOverDto(Guid CategoryId, string? Since);

    public sealed record BudgetMoveRequest(Guid FromCategoryId, Guid ToCategoryId, decimal Amount);

    /// <summary>Budgets propres à la période des deux catégories, après le déplacement.</summary>
    public sealed record BudgetMoveDto(PeriodBudgetDto From, PeriodBudgetDto To);

    public sealed record KindTotalsDto(decimal Income, decimal Fixed, decimal Variable, decimal Savings, decimal Remaining);

    /// <param name="Mode">Mode effectif (null : pas de budget).</param>
    /// <param name="IsDefaultMode">Mode appliqué par défaut (dépense fixe sans budget défini : Auto).</param>
    /// <param name="DefaultMode">Mode du budget par défaut défini (null si aucun).</param>
    /// <param name="Value">Valeur saisie du budget par défaut : montant ou pourcentage (null si aucun).</param>
    /// <param name="BudgetVersion">Version du budget par défaut, pour If-Match (null si aucun).</param>
    /// <param name="OwnBudgeted">Budget propre, hors sous-catégories, provisions comprises.</param>
    /// <param name="Budgeted">Budget, y compris celui des sous-catégories.</param>
    /// <param name="Actual">Réalisé, y compris les sous-catégories : positif pour une dépense comme pour un revenu.</param>
    /// <param name="PercentUsed">Part du budget consommée, en % (null sans budget).</param>
    /// <param name="CarriedOver">Reste reporté des périodes précédentes, compris dans le budget propre (D6).</param>
    /// <param name="CarryOverSince">Report activé depuis cette période (AAAA-MM), null sinon (D6).</param>
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
        BudgetStatus Status,
        DefinedBudgetDto? PeriodBudget,
        decimal Provisioned,
        BudgetMode? DefaultMode,
        decimal CarriedOver,
        string? CarryOverSince);

    /// <param name="IncomeBase">Revenus budgétés : la base des budgets en pourcentage.</param>
    /// <param name="Uncategorized">Somme signée des opérations sans catégorie (hors virements), hors récapitulatif.</param>
    /// <param name="Envelope">« À affecter » du mode enveloppes (D7) ; null s'il n'est pas actif sur la période.</param>
    public sealed record BudgetDto(
        PeriodEndpoints.PeriodDto Period,
        KindTotalsDto Budgeted,
        KindTotalsDto Actual,
        decimal IncomeBase,
        decimal Uncategorized,
        int WarningPercent,
        int AlertPercent,
        List<BudgetLineDto> Categories,
        EnvelopeSummary? Envelope);

    private static async Task<IResult> GetAsync(
        string periodKey, AppDbContext db, TimeProvider clock, IBudgetScheduleSource schedule, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = Domain.Periods.PeriodCalculator.From(settings);
        if (!calculator.TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }

        var (categories, budgets, overrides, result, uncategorized, envelope) = await ComputeAsync(db, settings, period, schedule, clock, ct);

        // Une catégorie archivée n'apparaît que si elle a encore un budget ou du réalisé sur la période.
        bool Visible(Category c) =>
            !c.IsArchived || result.Categories[c.Id] is { Budgeted: not 0 } or { Actual: not 0 };

        var lines = new List<BudgetLineDto>();
        var parents = categories.Where(c => c.ParentId is null).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name);
        foreach (var parent in parents.Where(Visible))
        {
            lines.Add(ToLine(parent, budgets.GetValueOrDefault(parent.Id), overrides.GetValueOrDefault(parent.Id), result.Categories[parent.Id]));
            lines.AddRange(categories.Where(c => c.ParentId == parent.Id && Visible(c))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => ToLine(c, budgets.GetValueOrDefault(c.Id), overrides.GetValueOrDefault(c.Id), result.Categories[c.Id])));
        }

        return TypedResults.Ok(new BudgetDto(
            PeriodEndpoints.ToDto(calculator, period, clock),
            ToDto(result.Budgeted),
            ToDto(result.Actual),
            result.IncomeBase,
            uncategorized,
            settings.BudgetWarningPercent,
            settings.BudgetAlertPercent,
            lines,
            envelope));
    }

    /// <summary>
    /// Calcul du budget d'une période, partagé avec la prévision, le tableau de bord et les alertes : reste reporté
    /// (D6) et « à affecter » (D7) compris.
    /// </summary>
    internal static async Task<BudgetComputation> ComputeAsync(
        AppDbContext db, Domain.Settings.HouseholdSettings settings, Domain.Periods.Period period, IBudgetScheduleSource schedule,
        TimeProvider clock, CancellationToken ct)
    {
        var book = await BudgetBook.LoadAsync(db, settings, schedule, ct);
        return await book.ComputeAsync(period, Accounts.Balances.Today(clock), ct);
    }

    /// <summary>
    /// Réalisé entre deux dates, regroupé en SQL par catégorie (somme signée), et somme des lignes sans catégorie.
    /// Les lignes de virement sans catégorie sont exclues (R5) ; seule la ligne d'un virement vers l'épargne porte
    /// une catégorie.
    /// </summary>
    internal static async Task<(Dictionary<Guid, decimal> ByCategory, decimal Uncategorized)> ActualSumsAsync(
        AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sums = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Date, IsTransfer = t.TransferId != null })
            .Where(x => x.Date >= from && x.Date <= to && !(x.CategoryId == null && x.IsTransfer))
            .GroupBy(x => x.CategoryId)
            .Select(g => new { CategoryId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        return (sums.Where(s => s.CategoryId is not null).ToDictionary(s => s.CategoryId!.Value, s => s.Sum),
            sums.Where(s => s.CategoryId is null).Sum(s => s.Sum));
    }

    /// <summary>Provisions de la période, par catégorie (D11).</summary>
    internal static Dictionary<Guid, ProvisionAmounts> Provisions(
        IEnumerable<Provision> provisions, Domain.Periods.PeriodCalculator calculator, Domain.Periods.Period period)
    {
        var result = new Dictionary<Guid, ProvisionAmounts>();
        foreach (var p in provisions)
        {
            if (ProvisionPlan.For(p.Amount, calculator.For(p.StartDate), calculator.For(p.DueDate), p.IsAnnual, period) is not { } share)
            {
                continue;
            }
            var current = result.GetValueOrDefault(p.CategoryId);
            result[p.CategoryId] = new ProvisionAmounts(
                current.Shares + share.Share,
                current.DueShares + (share.IsDue ? share.Share : 0m),
                current.DueAmounts + (share.IsDue ? p.Amount : 0m));
        }
        return result;
    }

    /// <summary>Budget appliqué : celui propre à la période, sinon celui par défaut.</summary>
    internal static BudgetCategory Definition(Category c, CategoryBudget? budget, PeriodBudget? period) =>
        period is not null ? new BudgetCategory(c.Id, c.ParentId, c.Kind, period.Mode, period.Value)
        : new BudgetCategory(c.Id, c.ParentId, c.Kind, budget?.Mode, budget?.Value ?? 0m);

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

    private static async Task<IResult> SetPeriodAsync(
        string periodKey, Guid categoryId, CategoryBudgetRequest request, HttpRequest http, AppDbContext db)
    {
        if (!(await PeriodEndpoints.CalculatorAsync(db)).TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
        {
            return TypedResults.NotFound();
        }
        var budget = await db.PeriodBudgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.PeriodKey == period.Key);
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
            budget = new PeriodBudget { CategoryId = categoryId, PeriodKey = period.Key };
            db.PeriodBudgets.Add(budget);
        }
        budget.Mode = request.Mode;
        budget.Value = request.Mode == BudgetMode.Auto ? 0m : request.Value;
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
        return TypedResults.Ok(new PeriodBudgetDto(budget.CategoryId, budget.PeriodKey, budget.Mode, budget.Value, budget.Version));
    }

    private static async Task<IResult> DeletePeriodAsync(string periodKey, Guid categoryId, HttpRequest http, AppDbContext db)
    {
        if (!(await PeriodEndpoints.CalculatorAsync(db)).TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }
        var budget = await db.PeriodBudgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.PeriodKey == period.Key);
        if (budget is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, budget) is { } precondition)
        {
            return precondition;
        }
        db.PeriodBudgets.Remove(budget);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<IResult> PreviewCopyAsync(string periodKey, BudgetCopyRequest request, AppDbContext db, CancellationToken ct)
    {
        var (proposals, problem) = await ProposeAsync(db, periodKey, request, ct);
        return problem ?? TypedResults.Ok(proposals!.Select(p => p.Dto).ToList());
    }

    private static async Task<IResult> CopyAsync(string periodKey, BudgetCopyRequest request, AppDbContext db, CancellationToken ct)
    {
        var (proposals, problem) = await ProposeAsync(db, periodKey, request, ct);
        if (problem is not null)
        {
            return problem;
        }
        var chosen = request.CategoryIds?.ToHashSet();
        var applied = 0;
        foreach (var p in proposals!.Where(p => chosen is null || chosen.Contains(p.Dto.CategoryId)))
        {
            var existing = await db.PeriodBudgets.FirstOrDefaultAsync(b => b.CategoryId == p.Dto.CategoryId && b.PeriodKey == p.PeriodKey, ct);
            // Revenir au budget par défaut supprime le budget propre à la période, plutôt que de le dupliquer.
            if (p.IsDefault)
            {
                if (existing is not null)
                {
                    db.PeriodBudgets.Remove(existing);
                }
            }
            else
            {
                if (existing is null)
                {
                    existing = new PeriodBudget { CategoryId = p.Dto.CategoryId, PeriodKey = p.PeriodKey };
                    db.PeriodBudgets.Add(existing);
                }
                existing.Mode = p.Dto.ProposedMode!.Value;
                existing.Value = p.Dto.ProposedValue ?? 0m;
            }
            applied++;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new BudgetCopyResultDto(applied));
    }

    private sealed record Proposal(BudgetCopyProposalDto Dto, string PeriodKey, bool IsDefault);

    /// <summary>
    /// Propositions de copie (D5) pour les catégories actives dont le budget changerait. Période précédente : son
    /// budget appliqué (propre ou par défaut). Moyenne : le réalisé propre de la catégorie sur les N périodes qui
    /// précèdent, divisé par N, en montant ; une moyenne nulle n'est pas proposée.
    /// </summary>
    private static async Task<(List<Proposal>? Proposals, IResult? Problem)> ProposeAsync(
        AppDbContext db, string periodKey, BudgetCopyRequest request, CancellationToken ct)
    {
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        if (!calculator.TryParse(periodKey, out var period))
        {
            return (null, PeriodEndpoints.UnknownPeriod());
        }
        var months = request.Months ?? 3;
        if (!Enum.IsDefined(request.Source)
            || (request.Source == BudgetCopySource.Average && months is < 1 or > MaxAverageMonths))
        {
            return (null, TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [Enum.IsDefined(request.Source) ? "months" : "source"] =
                    [Enum.IsDefined(request.Source) ? $"La moyenne porte sur 1 à {MaxAverageMonths} mois." : "Source inconnue."],
            }));
        }

        var categories = await db.Categories.AsNoTracking().Where(c => !c.IsArchived).ToListAsync(ct);
        var defaults = await db.CategoryBudgets.AsNoTracking().ToDictionaryAsync(b => b.CategoryId, ct);
        var current = await db.PeriodBudgets.AsNoTracking().Where(b => b.PeriodKey == period.Key).ToDictionaryAsync(b => b.CategoryId, ct);
        var previous = calculator.Previous(period);
        var source = request.Source == BudgetCopySource.PreviousPeriod
            ? await db.PeriodBudgets.AsNoTracking().Where(b => b.PeriodKey == previous.Key).ToDictionaryAsync(b => b.CategoryId, ct)
            : [];
        Dictionary<Guid, decimal> averages = [];
        if (request.Source == BudgetCopySource.Average)
        {
            var first = previous;
            for (var i = 1; i < months; i++)
            {
                first = calculator.Previous(first);
            }
            var (sums, _) = await ActualSumsAsync(db, first.Start, previous.End, ct);
            averages = sums.ToDictionary(s => s.Key, s => s.Value / months);
        }
        var scheduled = await new Recurrences.RecurrenceBudgetSource(db).GetScheduledSumsAsync(period, ct);

        var proposals = new List<Proposal>();
        foreach (var c in categories.OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var sign = c.Kind == CategoryKind.Income ? 1m : -1m;
            var now = Definition(c, defaults.GetValueOrDefault(c.Id), current.GetValueOrDefault(c.Id));
            var nowMode = now.Mode ?? (c.Kind == CategoryKind.Fixed ? BudgetMode.Auto : null);
            BudgetMode? mode;
            decimal value;
            if (request.Source == BudgetCopySource.PreviousPeriod)
            {
                var before = Definition(c, defaults.GetValueOrDefault(c.Id), source.GetValueOrDefault(c.Id));
                mode = before.Mode ?? (c.Kind == CategoryKind.Fixed ? BudgetMode.Auto : null);
                value = mode == BudgetMode.Auto ? 0m : before.Value;
            }
            else
            {
                value = Math.Round(sign * averages.GetValueOrDefault(c.Id), 2, MidpointRounding.AwayFromZero);
                if (value <= 0)
                {
                    continue;
                }
                mode = BudgetMode.Amount;
            }
            var nowValue = nowMode == BudgetMode.Auto ? 0m : now.Value;
            if (mode == nowMode && value == nowValue)
            {
                continue;
            }
            // Sans budget à reprendre, seul un retour au budget par défaut a un sens.
            var defaultBudget = defaults.GetValueOrDefault(c.Id);
            var defaultMode = defaultBudget?.Mode ?? (c.Kind == CategoryKind.Fixed ? BudgetMode.Auto : null);
            var isDefault = mode == defaultMode && value == (defaultMode == BudgetMode.Auto ? 0m : defaultBudget?.Value ?? 0m);
            if (mode is null && !isDefault)
            {
                continue;
            }
            var currentBudgeted = nowMode switch
            {
                BudgetMode.Amount => now.Value,
                BudgetMode.Auto => sign * scheduled.GetValueOrDefault(c.Id),
                _ => 0m,
            };
            proposals.Add(new Proposal(
                new BudgetCopyProposalDto(
                    c.Id, c.ParentId, c.Name, c.Kind, nowMode, nowMode is null or BudgetMode.Auto ? null : now.Value,
                    currentBudgeted, mode, mode is null or BudgetMode.Auto ? null : value),
                period.Key,
                isDefault));
        }
        return (proposals, null);
    }

    private static async Task<IResult> SetCarryOverAsync(
        Guid categoryId, CarryOverRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null)
        {
            return TypedResults.NotFound();
        }
        if (!request.Enabled)
        {
            category.CarryOverSince = null;
            return await db.TrySaveAsync() ? TypedResults.Ok(new CarryOverDto(category.Id, null)) : Concurrency.Conflict();
        }

        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        var current = calculator.For(Accounts.Balances.Today(clock));
        string? error = null;
        var since = current;
        if (!BudgetBook.CanCarryOver(category))
        {
            error = "Seules les dépenses (fixes et variables) reportent leur reste.";
        }
        else if (category.IsArchived)
        {
            error = "Une catégorie archivée ne reporte pas son reste.";
        }
        else if (request.Since is not null && !calculator.TryParse(request.Since, out since))
        {
            error = "Période inconnue.";
        }
        else if (string.CompareOrdinal(since.Key, current.Key) > 0)
        {
            error = "Le report ne peut pas commencer après la période en cours.";
        }
        if (error is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["since"] = [error] });
        }
        // Réactiver garde la période d'origine, sauf si une autre est demandée.
        category.CarryOverSince = request.Since is null ? category.CarryOverSince ?? since.Key : since.Key;
        return await db.TrySaveAsync() ? TypedResults.Ok(new CarryOverDto(category.Id, category.CarryOverSince)) : Concurrency.Conflict();
    }

    /// <summary>
    /// Déplace un montant d'une enveloppe à l'autre (D7) : les deux budgets deviennent des montants propres à la
    /// période (D4), calculés depuis leur budget actuel (hors provisions et reste reporté).
    /// </summary>
    private static async Task<IResult> MoveAsync(
        string periodKey, BudgetMoveRequest request, AppDbContext db, IBudgetScheduleSource schedule, TimeProvider clock, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var book = await BudgetBook.LoadAsync(db, settings, schedule, ct);
        if (!book.Calculator.TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }
        var (categories, _, _, result, _, _) = await book.ComputeAsync(period, Accounts.Balances.Today(clock), ct);
        var from = categories.FirstOrDefault(c => c.Id == request.FromCategoryId);
        var to = categories.FirstOrDefault(c => c.Id == request.ToCategoryId);
        if (from is null || to is null)
        {
            return TypedResults.NotFound();
        }

        // Budget propre de la période, sans les provisions ni le reste reporté, qui ne se déplacent pas.
        decimal Own(Category c)
        {
            var r = result.Categories[c.Id];
            return r.OwnBudgeted - r.OwnProvisioned - r.OwnCarriedOver;
        }
        var (field, error) = request switch
        {
            _ when from.Id == to.Id => ("toCategoryId", "Choisir deux catégories différentes."),
            _ when !Assignable(from) || !Assignable(to) =>
                ("toCategoryId", "Seules les dépenses et l'épargne d'une catégorie active reçoivent un budget à déplacer."),
            { Amount: <= 0 or > MaxAmount } => ("amount", "Le montant doit être positif."),
            _ when request.Amount != Math.Round(request.Amount, 2) => ("amount", "Deux décimales au maximum."),
            _ when request.Amount > Own(from) => ("amount", $"Le budget de « {from.Name} » sur la période ne suffit pas."),
            _ => (null, null),
        };
        if (error is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field!] = [error] });
        }

        var source = await SetAmountAsync(db, from.Id, period.Key, Own(from) - request.Amount, ct);
        var target = await SetAmountAsync(db, to.Id, period.Key, Own(to) + request.Amount, ct);
        if (target.Value > MaxAmount)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Le montant doit être positif."] });
        }
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
        return TypedResults.Ok(new BudgetMoveDto(ToDto(source), ToDto(target)));

        static bool Assignable(Category c) => !c.IsArchived && c.Kind != CategoryKind.Income;
        static PeriodBudgetDto ToDto(PeriodBudget b) => new(b.CategoryId, b.PeriodKey, b.Mode, b.Value, b.Version);
    }

    private static async Task<PeriodBudget> SetAmountAsync(AppDbContext db, Guid categoryId, string periodKey, decimal amount, CancellationToken ct)
    {
        var budget = await db.PeriodBudgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.PeriodKey == periodKey, ct);
        if (budget is null)
        {
            budget = new PeriodBudget { CategoryId = categoryId, PeriodKey = periodKey };
            db.PeriodBudgets.Add(budget);
        }
        budget.Mode = BudgetMode.Amount;
        budget.Value = amount;
        return budget;
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

    private static BudgetLineDto ToLine(Category c, CategoryBudget? budget, PeriodBudget? period, CategoryBudgetResult r) =>
        new(c.Id, c.ParentId, c.Name, c.Kind, c.Color, c.Icon, c.IsArchived,
            r.Mode, r.IsDefaultMode, budget?.Value, budget?.Version,
            r.OwnBudgeted, r.Budgeted, r.Actual, r.Remaining, r.PercentUsed, r.Status,
            period is null ? null : new DefinedBudgetDto(period.Mode, period.Value, period.Version),
            r.OwnProvisioned,
            budget?.Mode,
            r.OwnCarriedOver,
            c.CarryOverSince);

    private static KindTotalsDto ToDto(KindTotals t) => new(t.Income, t.Fixed, t.Variable, t.Savings, t.Remaining);
}
