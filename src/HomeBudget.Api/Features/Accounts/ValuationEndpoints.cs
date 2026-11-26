using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>
/// Valorisations manuelles des comptes d'investissement (A7) : l'historique des valeurs saisies. Le solde du compte
/// part de la dernière valorisation et y ajoute les opérations datées après elle. Une valorisation suit la visibilité
/// de son compte (R8).
/// </summary>
public static class ValuationEndpoints
{
    private const decimal MaxValue = 999_999_999m;

    public static IEndpointRouteBuilder MapValuationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}/valuations").WithTags("Accounts");

        group.MapGet("/", ListAsync)
            .Produces<List<ValuationDto>>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Valorisations du compte, de la plus récente à la plus ancienne.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ValuationDto>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Saisit la valeur d'un compte d'investissement à une date.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ValuationDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une valorisation (If-Match requis).");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une valorisation (If-Match requis).");

        return app;
    }

    /// <param name="Date">Jour de la valeur : elle vaut à la fin de ce jour, opérations du jour comprises.</param>
    public sealed record ValuationRequest(
        DateOnly Date,
        decimal Value,
        [property: StringLength(AccountValuation.MaxNoteLength)] string? Note);

    /// <param name="Change">Écart avec la valorisation précédente (null pour la première).</param>
    public sealed record ValuationDto(Guid Id, Guid AccountId, DateOnly Date, decimal Value, string? Note, decimal? Change, int Version);

    private static async Task<IResult> ListAsync(Guid accountId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
        {
            return TypedResults.NotFound();
        }
        var valuations = await db.AccountValuations.AsNoTracking()
            .Where(v => v.AccountId == accountId).OrderBy(v => v.Date).ToListAsync(ct);
        var result = new List<ValuationDto>();
        for (var i = 0; i < valuations.Count; i++)
        {
            result.Add(ToDto(valuations[i], i == 0 ? null : valuations[i - 1].Value));
        }
        result.Reverse();
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        Guid accountId, ValuationRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        if (await ValidateAsync(request, account, null, db, clock, ct) is { } problem)
        {
            return problem;
        }
        var valuation = new AccountValuation { AccountId = accountId };
        Apply(valuation, request);
        db.AccountValuations.Add(valuation);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/accounts/{accountId}/valuations/{valuation.Id}", await WithChangeAsync(db, valuation, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid accountId, Guid id, ValuationRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var valuation = await db.AccountValuations.FirstOrDefaultAsync(v => v.Id == id && v.AccountId == accountId, ct);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (valuation is null || account is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, valuation) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, account, valuation, db, clock, ct) is { } problem)
        {
            return problem;
        }
        Apply(valuation, request);
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(await WithChangeAsync(db, valuation, ct)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid accountId, Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var valuation = await db.AccountValuations.FirstOrDefaultAsync(v => v.Id == id && v.AccountId == accountId, ct);
        if (valuation is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, valuation) is { } precondition)
        {
            return precondition;
        }
        db.AccountValuations.Remove(valuation);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<ValidationProblem?> ValidateAsync(
        ValuationRequest request, Account account, AccountValuation? existing, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (account.Type != AccountType.Investment)
        {
            errors["accountId"] = ["Seuls les comptes d'investissement se valorisent."];
        }
        else if (account.IsArchived && existing is null)
        {
            errors["accountId"] = ["Ce compte est archivé."];
        }
        if (Math.Abs(request.Value) > MaxValue || !TransactionValidation.HasCents(request.Value))
        {
            errors["value"] = ["La valeur a au plus 2 décimales."];
        }
        if (request.Date > Balances.Today(clock))
        {
            errors["date"] = ["La date ne peut pas être dans le futur."];
        }
        else if (request.Date < account.InitialDate)
        {
            errors["date"] = ["La date précède l'ouverture du compte."];
        }
        else if (await db.AccountValuations.AnyAsync(
            v => v.AccountId == account.Id && v.Date == request.Date && (existing == null || v.Id != existing.Id), ct))
        {
            errors["date"] = ["Une valorisation existe déjà à cette date."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void Apply(AccountValuation valuation, ValuationRequest request)
    {
        valuation.Date = request.Date;
        valuation.Value = request.Value;
        valuation.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    }

    private static async Task<ValuationDto> WithChangeAsync(AppDbContext db, AccountValuation valuation, CancellationToken ct)
    {
        var previous = await db.AccountValuations.AsNoTracking()
            .Where(v => v.AccountId == valuation.AccountId && v.Date < valuation.Date)
            .OrderByDescending(v => v.Date)
            .Select(v => (decimal?)v.Value)
            .FirstOrDefaultAsync(ct);
        return ToDto(valuation, previous);
    }

    private static ValuationDto ToDto(AccountValuation v, decimal? previous) =>
        new(v.Id, v.AccountId, v.Date, v.Value, v.Note, previous is { } p ? v.Value - p : null, v.Version);
}
