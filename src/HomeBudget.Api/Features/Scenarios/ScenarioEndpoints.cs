using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Scenarios;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Scenarios;

/// <summary>
/// Scénarios « et si… » (F5) : événements fictifs, nommés et activables, ajoutés à la prévision dans une série à
/// part. Ils ne sont jamais mélangés aux opérations, budgets, totaux ou exports. Un événement suit la visibilité
/// de son compte (R8).
/// </summary>
public static class ScenarioEndpoints
{
    private const decimal MaxAmount = 10_000_000m;

    public static IEndpointRouteBuilder MapScenarioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scenarios").WithTags("Scenarios");

        group.MapGet("/", ListAsync)
            .WithSummary("Scénarios, avec leurs événements visibles, par nom.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée un scénario.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ScenarioDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Renomme, active ou désactive un scénario (If-Match requis).");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un scénario et tous ses événements (If-Match requis).");
        group.MapPost("/{id:guid}/events", CreateEventAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ScenarioEventDto>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Ajoute un événement fictif au scénario.");
        group.MapPut("/{id:guid}/events/{eventId:guid}", UpdateEventAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ScenarioEventDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un événement du scénario (If-Match requis).");
        group.MapDelete("/{id:guid}/events/{eventId:guid}", DeleteEventAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un événement du scénario (If-Match requis).");

        return app;
    }

    public sealed record ScenarioRequest(
        [property: Required, StringLength(Scenario.MaxNameLength, MinimumLength = 1)] string Name,
        bool IsActive);

    /// <param name="Amount">Montant signé : négatif pour une sortie.</param>
    /// <param name="Date">Date de l'événement ; pour un événement mensuel, celle de la première fois.</param>
    /// <param name="EndDate">Dernier jour possible d'un événement mensuel (ignoré pour un événement ponctuel).</param>
    public sealed record ScenarioEventRequest(
        [property: Required, StringLength(ScenarioEvent.MaxLabelLength, MinimumLength = 1)] string Label,
        Guid AccountId,
        Guid? CategoryId,
        decimal Amount,
        DateOnly Date,
        ScenarioEventFrequency Frequency,
        DateOnly? EndDate);

    public sealed record ScenarioEventDto(
        Guid Id,
        Guid ScenarioId,
        string Label,
        Guid AccountId,
        Guid? CategoryId,
        decimal Amount,
        DateOnly Date,
        ScenarioEventFrequency Frequency,
        DateOnly? EndDate,
        int Version);

    public sealed record ScenarioDto(Guid Id, string Name, bool IsActive, int Version, List<ScenarioEventDto> Events);

    private static async Task<Ok<List<ScenarioDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var scenarios = await db.Scenarios.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var events = (await db.ScenarioEvents.AsNoTracking().ToListAsync(ct)).ToLookup(e => e.ScenarioId);
        return TypedResults.Ok(scenarios.ConvertAll(s => ToDto(s, events[s.Id])));
    }

    private static async Task<Results<Created<ScenarioDto>, ValidationProblem>> CreateAsync(ScenarioRequest request, AppDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }
        var scenario = new Scenario { Name = request.Name.Trim(), IsActive = request.IsActive };
        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/scenarios/{scenario.Id}", ToDto(scenario, []));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ScenarioRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var scenario = await db.Scenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, scenario) is { } precondition)
        {
            return precondition;
        }
        if (Validate(request) is { } problem)
        {
            return problem;
        }
        scenario.Name = request.Name.Trim();
        scenario.IsActive = request.IsActive;
        if (!await db.TrySaveAsync(ct))
        {
            return Concurrency.Conflict();
        }
        var events = await db.ScenarioEvents.AsNoTracking().Where(e => e.ScenarioId == id).ToListAsync(ct);
        return TypedResults.Ok(ToDto(scenario, events));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var scenario = await db.Scenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, scenario) is { } precondition)
        {
            return precondition;
        }
        // Les événements fictifs partent avec le scénario, y compris ceux des comptes privés des autres membres.
        db.Scenarios.Remove(scenario);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<IResult> CreateEventAsync(Guid id, ScenarioEventRequest request, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Scenarios.AnyAsync(s => s.Id == id, ct))
        {
            return TypedResults.NotFound();
        }
        if (await ValidateAsync(request, db, ct) is { } problem)
        {
            return problem;
        }
        var scenarioEvent = new ScenarioEvent { ScenarioId = id, Label = request.Label.Trim() };
        Apply(scenarioEvent, request);
        db.ScenarioEvents.Add(scenarioEvent);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/scenarios/{id}/events/{scenarioEvent.Id}", ToDto(scenarioEvent));
    }

    private static async Task<IResult> UpdateEventAsync(
        Guid id, Guid eventId, ScenarioEventRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var scenarioEvent = await db.ScenarioEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.ScenarioId == id, ct);
        if (scenarioEvent is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, scenarioEvent) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, ct) is { } problem)
        {
            return problem;
        }
        Apply(scenarioEvent, request);
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(ToDto(scenarioEvent)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteEventAsync(Guid id, Guid eventId, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var scenarioEvent = await db.ScenarioEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.ScenarioId == id, ct);
        if (scenarioEvent is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, scenarioEvent) is { } precondition)
        {
            return precondition;
        }
        db.ScenarioEvents.Remove(scenarioEvent);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static ValidationProblem? Validate(ScenarioRequest request) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Le nom est obligatoire."] })
            : null;

    private static async Task<ValidationProblem?> ValidateAsync(ScenarioEventRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            errors["label"] = ["Le libellé est obligatoire."];
        }
        if (request.Amount == 0 || Math.Abs(request.Amount) > MaxAmount)
        {
            errors["amount"] = ["Le montant doit être non nul."];
        }
        else if (!TransactionValidation.HasCents(request.Amount))
        {
            errors["amount"] = ["Le montant a au plus 2 décimales."];
        }
        // Le filtre de visibilité (R8) écarte les comptes privés des autres membres.
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);
        if (account is null)
        {
            errors["accountId"] = ["Compte introuvable."];
        }
        else if (account.IsArchived)
        {
            errors["accountId"] = ["Ce compte est archivé."];
        }
        if (request.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            errors["categoryId"] = ["Catégorie introuvable."];
        }
        if (!Enum.IsDefined(request.Frequency))
        {
            errors["frequency"] = ["Fréquence inconnue."];
        }
        else if (request.Frequency == ScenarioEventFrequency.Monthly && request.EndDate < request.Date)
        {
            errors["endDate"] = ["La date de fin doit suivre la première date."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void Apply(ScenarioEvent e, ScenarioEventRequest request)
    {
        e.Label = request.Label.Trim();
        e.AccountId = request.AccountId;
        e.CategoryId = request.CategoryId;
        e.Amount = request.Amount;
        e.Date = request.Date;
        e.Frequency = request.Frequency;
        e.EndDate = request.Frequency == ScenarioEventFrequency.Monthly ? request.EndDate : null;
    }

    private static ScenarioDto ToDto(Scenario s, IEnumerable<ScenarioEvent> events) =>
        new(s.Id, s.Name, s.IsActive, s.Version, [.. events.OrderBy(e => e.Date).ThenBy(e => e.Label).Select(ToDto)]);

    private static ScenarioEventDto ToDto(ScenarioEvent e) =>
        new(e.Id, e.ScenarioId, e.Label, e.AccountId, e.CategoryId, e.Amount, e.Date, e.Frequency, e.EndDate, e.Version);
}
