using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Savings;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Savings;

/// <summary>
/// Objectifs d'épargne (J1) avec leur jauge et le versement mensuel conseillé (J2). Un objectif se mesure au solde
/// d'un compte (par exemple le livret d'un enfant, J4) ou au cumul d'une catégorie d'épargne depuis une date.
/// Rattaché à un compte, il suit la visibilité de ce compte (R8) ; rattaché à une catégorie, son cumul ne compte que
/// les opérations visibles.
/// </summary>
public static class SavingsGoalEndpoints
{
    private const decimal MaxAmount = 999_999_999m;

    public static IEndpointRouteBuilder MapSavingsGoalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/savings-goals").WithTags("Savings");

        group.MapGet("/", ListAsync)
            .WithSummary("Objectifs d'épargne avec leur avancement, par date cible puis par nom.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée un objectif d'épargne.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<SavingsGoalDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un objectif d'épargne (If-Match requis). L'archivage passe par isArchived.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un objectif d'épargne (If-Match requis).");

        return app;
    }

    /// <param name="AccountId">Compte dont le solde mesure l'épargne réunie (exclusif de categoryId).</param>
    /// <param name="CategoryId">Catégorie d'épargne dont le cumul mesure l'épargne réunie (exclusif de accountId).</param>
    /// <param name="StartDate">Début du cumul de la catégorie ; aujourd'hui par défaut. Ignoré pour un compte.</param>
    public sealed record SavingsGoalRequest(
        [property: Required, StringLength(SavingsGoal.MaxNameLength, MinimumLength = 1)] string Name,
        decimal TargetAmount,
        DateOnly? TargetDate,
        Guid? AccountId,
        Guid? CategoryId,
        DateOnly? StartDate,
        [property: StringLength(16)] string? Color,
        [property: StringLength(40)] string? Icon,
        bool IsArchived = false);

    /// <param name="Saved">Épargne réunie : solde du compte, ou cumul de la catégorie depuis startDate.</param>
    /// <param name="Remaining">Reste à réunir (0 si la cible est atteinte).</param>
    /// <param name="Percent">Avancement, de 0 à 100.</param>
    /// <param name="RemainingPeriods">Périodes restantes jusqu'à la date cible, la période en cours comprise.</param>
    /// <param name="MonthlyContribution">Versement conseillé par période (J2) : null sans date cible ou si elle est passée.</param>
    public sealed record SavingsGoalDto(
        Guid Id,
        string Name,
        decimal TargetAmount,
        DateOnly? TargetDate,
        Guid? AccountId,
        Guid? CategoryId,
        DateOnly StartDate,
        string? Color,
        string? Icon,
        bool IsArchived,
        decimal Saved,
        decimal Remaining,
        int Percent,
        SavingsGoalStatus Status,
        int? RemainingPeriods,
        decimal? MonthlyContribution,
        int Version);

    private static async Task<Ok<List<SavingsGoalDto>>> ListAsync(AppDbContext db, TimeProvider clock, bool? includeArchived, CancellationToken ct)
    {
        var goals = await db.SavingsGoals.AsNoTracking()
            .Where(g => includeArchived == true || !g.IsArchived)
            .ToListAsync(ct);
        var result = await ToDtosAsync(db, clock, goals, ct);
        return TypedResults.Ok(result
            .OrderBy(g => g.IsArchived).ThenBy(g => g.TargetDate ?? DateOnly.MaxValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<Results<Created<SavingsGoalDto>, ValidationProblem>> CreateAsync(
        SavingsGoalRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (await ValidateAsync(request, db, null, ct) is { } problem)
        {
            return problem;
        }
        var goal = new SavingsGoal { Name = request.Name.Trim() };
        Apply(goal, request, Balances.Today(clock));
        db.SavingsGoals.Add(goal);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/savings-goals/{goal.Id}", (await ToDtosAsync(db, clock, [goal], ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SavingsGoalRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var goal = await db.SavingsGoals.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, goal) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, goal, ct) is { } problem)
        {
            return problem;
        }
        Apply(goal, request, goal.StartDate);
        return await db.TrySaveAsync(ct) ? TypedResults.Ok((await ToDtosAsync(db, clock, [goal], ct))[0]) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var goal = await db.SavingsGoals.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (goal is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, goal) is { } precondition)
        {
            return precondition;
        }
        db.SavingsGoals.Remove(goal);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<ValidationProblem?> ValidateAsync(
        SavingsGoalRequest request, AppDbContext db, SavingsGoal? existing, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Le nom est obligatoire."];
        }
        if (request.TargetAmount <= 0 || request.TargetAmount > MaxAmount)
        {
            errors["targetAmount"] = ["Le montant cible doit être positif."];
        }
        else if (!TransactionValidation.HasCents(request.TargetAmount))
        {
            errors["targetAmount"] = ["Le montant a au plus 2 décimales."];
        }
        if (request.TargetDate is { } date && (date < TransactionValidation.MinDate || date > TransactionValidation.MaxDate))
        {
            errors["targetDate"] = ["Date cible invalide."];
        }
        if ((request.AccountId is null) == (request.CategoryId is null))
        {
            errors["accountId"] = ["Choisissez un compte ou une catégorie d'épargne, l'un ou l'autre."];
        }
        else if (request.AccountId is { } accountId)
        {
            // Le filtre de visibilité (R8) écarte les comptes privés des autres membres.
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null)
            {
                errors["accountId"] = ["Compte introuvable."];
            }
            else if (account.IsArchived && existing?.AccountId != accountId)
            {
                errors["accountId"] = ["Ce compte est archivé."];
            }
        }
        else if (request.CategoryId is { } categoryId)
        {
            var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null)
            {
                errors["categoryId"] = ["Catégorie introuvable."];
            }
            else if (category.Kind != CategoryKind.Savings)
            {
                errors["categoryId"] = ["Choisissez une catégorie d'épargne."];
            }
        }
        if (request.StartDate is { } start && (start < TransactionValidation.MinDate || start > TransactionValidation.MaxDate))
        {
            errors["startDate"] = ["Date de début invalide."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void Apply(SavingsGoal goal, SavingsGoalRequest request, DateOnly defaultStart)
    {
        goal.Name = request.Name.Trim();
        goal.TargetAmount = request.TargetAmount;
        goal.TargetDate = request.TargetDate;
        goal.AccountId = request.AccountId;
        goal.CategoryId = request.AccountId is null ? request.CategoryId : null;
        goal.StartDate = request.StartDate ?? defaultStart;
        goal.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        goal.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        goal.IsArchived = request.IsArchived;
    }

    /// <summary>Avancement des objectifs : soldes des comptes rattachés, cumuls des catégories rattachées.</summary>
    private static async Task<List<SavingsGoalDto>> ToDtosAsync(
        AppDbContext db, TimeProvider clock, IReadOnlyList<SavingsGoal> goals, CancellationToken ct)
    {
        var today = Balances.Today(clock);
        var calculator = await PeriodEndpoints.CalculatorAsync(db);

        var accountIds = goals.Where(g => g.AccountId is not null).Select(g => g.AccountId!.Value).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToListAsync(ct);
        var balances = await Balances.ComputeAsync(db, clock, accounts, ct);

        var categories = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        var saved = new Dictionary<Guid, decimal>();
        foreach (var goal in goals)
        {
            if (goal.AccountId is { } accountId)
            {
                saved[goal.Id] = balances.TryGetValue(accountId, out var balance) ? balance.Current : 0m;
            }
            else if (goal.CategoryId is { } categoryId)
            {
                var ids = categories.Where(c => c.Id == categoryId || c.ParentId == categoryId).Select(c => (Guid?)c.Id).ToList();
                var start = goal.StartDate;
                // Une ligne d'épargne est une sortie (montant négatif) : l'épargne réunie en est l'opposé.
                saved[goal.Id] = -await db.TransactionLines.AsNoTracking()
                    .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Date })
                    .Where(x => ids.Contains(x.CategoryId) && x.Date >= start && x.Date <= today)
                    .SumAsync(x => x.Amount, ct);
            }
        }
        return [.. goals.Select(g => ToDto(g, SavingsGoalPlan.Compute(g.TargetAmount, saved.GetValueOrDefault(g.Id), g.TargetDate, today, calculator)))];
    }

    private static SavingsGoalDto ToDto(SavingsGoal g, SavingsGoalProgress p) =>
        new(g.Id, g.Name, g.TargetAmount, g.TargetDate, g.AccountId, g.CategoryId, g.StartDate, g.Color, g.Icon, g.IsArchived,
            p.Saved, p.Remaining, p.Percent, p.Status, p.RemainingPeriods, p.MonthlyContribution, g.Version);
}
