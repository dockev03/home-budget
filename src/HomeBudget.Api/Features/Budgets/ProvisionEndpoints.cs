using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Budgets;

/// <summary>Provisions (D11) : dépenses annuelles ou ponctuelles lissées par période dans le budget.</summary>
public static class ProvisionEndpoints
{
    private const decimal MaxAmount = 10_000_000m;

    public static IEndpointRouteBuilder MapProvisionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/provisions").WithTags("Provisions");

        group.MapGet("/", ListAsync)
            .WithSummary("Provisions, avec leur part et leur cumul pour la période en cours, par prochaine échéance.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée une provision.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ProvisionDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une provision (If-Match requis).");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une provision (If-Match requis).");

        return app;
    }

    /// <param name="Amount">Montant de la dépense, positif.</param>
    /// <param name="DueDate">Date de la dépense ; pour une provision annuelle, celle de la prochaine échéance.</param>
    /// <param name="StartDate">Début du lissage ; aujourd'hui par défaut.</param>
    public sealed record ProvisionRequest(
        [property: Required, StringLength(Provision.MaxLabelLength, MinimumLength = 1)] string Label,
        Guid CategoryId,
        [property: Range(0.01, 999_999_999)] decimal Amount,
        DateOnly DueDate,
        bool IsAnnual,
        DateOnly? StartDate);

    /// <param name="Share">Part réservée dans la période en cours (0 hors lissage).</param>
    /// <param name="Saved">Cumul réservé du cycle en cours, période en cours comprise.</param>
    /// <param name="Periods">Nombre de périodes du cycle en cours (0 si la provision est terminée ou pas commencée).</param>
    /// <param name="DueKey">Période de la prochaine échéance (null si la provision ponctuelle est passée).</param>
    public sealed record ProvisionDto(
        Guid Id,
        string Label,
        Guid CategoryId,
        decimal Amount,
        DateOnly DueDate,
        bool IsAnnual,
        DateOnly StartDate,
        decimal Share,
        decimal Saved,
        int Periods,
        string? DueKey,
        int Version);

    private static async Task<Ok<List<ProvisionDto>>> ListAsync(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        var current = calculator.For(Balances.Today(clock));
        var provisions = await db.Provisions.AsNoTracking().ToListAsync(ct);
        return TypedResults.Ok(provisions.Select(p => ToDto(p, calculator, current))
            .OrderBy(p => p.DueKey is null).ThenBy(p => p.DueKey, StringComparer.Ordinal).ThenBy(p => p.Label).ToList());
    }

    private static async Task<Results<Created<ProvisionDto>, ValidationProblem>> CreateAsync(
        ProvisionRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        var startDate = request.StartDate ?? Balances.Today(clock);
        if (await ValidateAsync(request, startDate, calculator, db, ct) is { } problem)
        {
            return problem;
        }
        var provision = new Provision { Label = request.Label.Trim() };
        Apply(provision, request, startDate);
        db.Provisions.Add(provision);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/provisions/{provision.Id}", ToDto(provision, calculator, calculator.For(Balances.Today(clock))));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, ProvisionRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var provision = await db.Provisions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (provision is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, provision) is { } precondition)
        {
            return precondition;
        }
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        var startDate = request.StartDate ?? provision.StartDate;
        if (await ValidateAsync(request, startDate, calculator, db, ct) is { } problem)
        {
            return problem;
        }
        Apply(provision, request, startDate);
        if (!await db.TrySaveAsync(ct))
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok(ToDto(provision, calculator, calculator.For(Balances.Today(clock))));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var provision = await db.Provisions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (provision is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, provision) is { } precondition)
        {
            return precondition;
        }
        db.Provisions.Remove(provision);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<ValidationProblem?> ValidateAsync(
        ProvisionRequest request, DateOnly startDate, PeriodCalculator calculator, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            errors["label"] = ["Le libellé est obligatoire."];
        }
        if (request.Amount is <= 0 or > MaxAmount)
        {
            errors["amount"] = ["Le montant doit être positif."];
        }
        else if (!TransactionValidation.HasCents(request.Amount))
        {
            errors["amount"] = ["Le montant a au plus 2 décimales."];
        }
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null)
        {
            errors["categoryId"] = ["Catégorie introuvable."];
        }
        else if (category.Kind is not (CategoryKind.Fixed or CategoryKind.Variable))
        {
            errors["categoryId"] = ["Une provision porte sur une catégorie de dépense."];
        }
        if (!request.IsAnnual && calculator.For(request.DueDate).Start < calculator.For(startDate).Start)
        {
            errors["dueDate"] = ["L'échéance doit suivre le début du lissage."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void Apply(Provision provision, ProvisionRequest request, DateOnly startDate)
    {
        provision.Label = request.Label.Trim();
        provision.CategoryId = request.CategoryId;
        provision.Amount = request.Amount;
        provision.DueDate = request.DueDate;
        provision.IsAnnual = request.IsAnnual;
        provision.StartDate = startDate;
    }

    private static ProvisionDto ToDto(Provision p, PeriodCalculator calculator, Period current)
    {
        var start = calculator.For(p.StartDate);
        var due = calculator.For(p.DueDate);
        // Avant le début du lissage, le premier cycle est décrit depuis sa première période.
        var reference = current.Start < start.Start ? start : current;
        var share = ProvisionPlan.For(p.Amount, start, due, p.IsAnnual, reference);
        var started = reference.Start == current.Start;
        return new ProvisionDto(
            p.Id, p.Label, p.CategoryId, p.Amount, p.DueDate, p.IsAnnual, p.StartDate,
            started ? share?.Share ?? 0m : 0m,
            started ? share?.Saved ?? 0m : 0m,
            share?.Periods ?? 0,
            share?.DueKey,
            p.Version);
    }
}
