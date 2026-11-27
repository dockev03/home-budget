using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>
/// Rémunération des livrets (A9) : taux d'un compte d'épargne, passés et programmés, et intérêts par quinzaine
/// (<see cref="SavingsInterest"/>). Les intérêts calculés sont indicatifs : ils n'entrent ni dans le solde, ni dans le
/// patrimoine, ni dans la prévision ; l'opération réelle de la banque arrive par l'import ou la saisie. Les taux
/// suivent la visibilité de leur compte (R8).
/// </summary>
public static class SavingsInterestEndpoints
{
    public static IEndpointRouteBuilder MapSavingsInterestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}").WithTags("Accounts");

        group.MapGet("/savings-rates", ListAsync)
            .Produces<List<SavingsRateDto>>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Taux du livret, du plus récent (ou programmé) au plus ancien.");
        group.MapPost("/savings-rates", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<SavingsRateDto>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Ajoute un taux à une date, passée ou future (changement programmé).");
        group.MapPut("/savings-rates/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<SavingsRateDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un taux (If-Match requis).");
        group.MapDelete("/savings-rates/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un taux (If-Match requis).");
        group.MapGet("/interest", InterestAsync)
            .Produces<SavingsInterestDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Intérêts du livret par quinzaine sur une année (l'année en cours par défaut) : acquis à ce jour et estimés au 31 décembre.");

        return app;
    }

    /// <param name="EffectiveFrom">Premier jour du taux ; en cours de quinzaine, il s'applique à partir de la suivante.</param>
    /// <param name="AnnualRate">Taux annuel en pourcentage (4 décimales au plus).</param>
    public sealed record SavingsRateRequest(DateOnly EffectiveFrom, decimal AnnualRate);

    /// <param name="IsScheduled">Taux futur : un changement programmé.</param>
    public sealed record SavingsRateDto(Guid Id, Guid AccountId, DateOnly EffectiveFrom, decimal AnnualRate, bool IsScheduled, int Version);

    /// <param name="Base">Solde rémunéré de la quinzaine.</param>
    /// <param name="Interest">Intérêts de la quinzaine, arrondis au centime.</param>
    /// <param name="IsEnded">Quinzaine terminée : ses intérêts sont acquis.</param>
    public sealed record InterestFortnightDto(DateOnly Start, DateOnly End, decimal Base, decimal AnnualRate, decimal Interest, bool IsEnded);

    /// <param name="AsOf">Jour du calcul des intérêts acquis.</param>
    /// <param name="CurrentRate">Taux en vigueur ce jour-là (null sans aucun taux saisi).</param>
    /// <param name="NextRate">Prochain changement programmé, s'il y en a un.</param>
    /// <param name="Earned">Intérêts des quinzaines terminées de l'année.</param>
    /// <param name="Projected">Intérêts de toute l'année : opérations futures saisies et taux programmés compris.</param>
    public sealed record SavingsInterestDto(
        Guid AccountId,
        int Year,
        DateOnly AsOf,
        decimal? CurrentRate,
        SavingsRateDto? NextRate,
        decimal Earned,
        decimal Projected,
        List<InterestFortnightDto> Fortnights);

    /// <summary>Taux du jour et intérêts acquis cette année, par compte d'épargne qui a au moins un taux.</summary>
    public static async Task<Dictionary<Guid, (decimal Rate, decimal Earned)>> SummariesAsync(
        AppDbContext db, TimeProvider clock, IEnumerable<Account> accounts, CancellationToken ct = default)
    {
        var savings = accounts.Where(a => a.Type == AccountType.Savings).ToList();
        var ids = savings.Select(a => a.Id).ToList();
        var rates = (await db.SavingsRates.AsNoTracking().Where(r => ids.Contains(r.AccountId)).ToListAsync(ct))
            .ToLookup(r => r.AccountId);
        var withRates = savings.Where(a => rates.Contains(a.Id)).ToList();
        if (withRates.Count == 0)
        {
            return [];
        }
        var today = Balances.Today(clock);
        var movements = await MovementsAsync(db, withRates, ct);
        return withRates.ToDictionary(
            a => a.Id,
            a =>
            {
                var periods = rates[a.Id].Select(ToPeriod).ToList();
                var interest = SavingsInterest.Compute(today.Year, movements[a.Id], periods);
                return (SavingsInterest.RateAt(periods, today), interest.EarnedAt(today));
            });
    }

    private static async Task<IResult> ListAsync(Guid accountId, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
        {
            return TypedResults.NotFound();
        }
        var today = Balances.Today(clock);
        var rates = await db.SavingsRates.AsNoTracking()
            .Where(r => r.AccountId == accountId).OrderByDescending(r => r.EffectiveFrom).ToListAsync(ct);
        return TypedResults.Ok(rates.Select(r => ToDto(r, today)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        Guid accountId, SavingsRateRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        if (await ValidateAsync(request, account, null, db, ct) is { } problem)
        {
            return problem;
        }
        var rate = new SavingsRate { AccountId = accountId, EffectiveFrom = request.EffectiveFrom, AnnualRate = request.AnnualRate };
        db.SavingsRates.Add(rate);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/accounts/{accountId}/savings-rates/{rate.Id}", ToDto(rate, Balances.Today(clock)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid accountId, Guid id, SavingsRateRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var rate = await db.SavingsRates.FirstOrDefaultAsync(r => r.Id == id && r.AccountId == accountId, ct);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (rate is null || account is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, rate) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, account, rate, db, ct) is { } problem)
        {
            return problem;
        }
        rate.EffectiveFrom = request.EffectiveFrom;
        rate.AnnualRate = request.AnnualRate;
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(ToDto(rate, Balances.Today(clock))) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid accountId, Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var rate = await db.SavingsRates.FirstOrDefaultAsync(r => r.Id == id && r.AccountId == accountId, ct);
        if (rate is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, rate) is { } precondition)
        {
            return precondition;
        }
        db.SavingsRates.Remove(rate);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<IResult> InterestAsync(Guid accountId, int? year, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        var today = Balances.Today(clock);
        var errors = new Dictionary<string, string[]>();
        if (account.Type != AccountType.Savings)
        {
            errors["accountId"] = ["Seuls les comptes d'épargne ont des intérêts."];
        }
        var target = year ?? today.Year;
        if (target < TransactionValidation.MinDate.Year || target > TransactionValidation.MaxDate.Year)
        {
            errors["year"] = ["Année hors limites."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var rates = await db.SavingsRates.AsNoTracking().Where(r => r.AccountId == accountId).OrderBy(r => r.EffectiveFrom).ToListAsync(ct);
        var periods = rates.Select(ToPeriod).ToList();
        var interest = SavingsInterest.Compute(target, (await MovementsAsync(db, [account], ct))[account.Id], periods);
        var next = rates.FirstOrDefault(r => r.EffectiveFrom > today);
        return TypedResults.Ok(new SavingsInterestDto(
            account.Id,
            target,
            today,
            rates.Any(r => r.EffectiveFrom <= today) ? SavingsInterest.RateAt(periods, today) : null,
            next is null ? null : ToDto(next, today),
            interest.EarnedAt(today),
            interest.Total,
            [.. interest.Fortnights.Select(f => new InterestFortnightDto(
                f.Start, f.End, f.Base, f.AnnualRate, decimal.Round(f.Interest, 2, MidpointRounding.AwayFromZero), f.End <= today))]));
    }

    /// <summary>Mouvements de chaque compte : le solde initial à sa date d'ouverture, puis toutes ses opérations, futures comprises.</summary>
    private static async Task<Dictionary<Guid, List<SavingsMovement>>> MovementsAsync(
        AppDbContext db, IReadOnlyCollection<Account> accounts, CancellationToken ct)
    {
        var ids = accounts.Select(a => a.Id).ToList();
        var operations = (await db.Transactions.AsNoTracking()
                .Where(t => ids.Contains(t.AccountId))
                .Select(t => new { t.AccountId, t.Date, t.Amount })
                .ToListAsync(ct))
            .ToLookup(o => o.AccountId, o => new SavingsMovement(o.Date, o.Amount));
        return accounts.ToDictionary(
            a => a.Id,
            a => new List<SavingsMovement> { new(a.InitialDate, a.InitialBalance) }.Concat(operations[a.Id]).ToList());
    }

    private static async Task<IResult?> ValidateAsync(
        SavingsRateRequest request, Account account, SavingsRate? existing, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (account.Type != AccountType.Savings)
        {
            errors["accountId"] = ["Seuls les comptes d'épargne ont un taux de rémunération."];
        }
        else if (account.IsArchived && existing is null)
        {
            errors["accountId"] = ["Ce compte est archivé."];
        }
        if (request.AnnualRate is < 0 or > 100 || decimal.Round(request.AnnualRate, SavingsRate.RateDecimals) != request.AnnualRate)
        {
            errors["annualRate"] = [$"Le taux va de 0 à 100 %, avec {SavingsRate.RateDecimals} décimales au plus."];
        }
        if (request.EffectiveFrom < TransactionValidation.MinDate || request.EffectiveFrom > TransactionValidation.MaxDate)
        {
            errors["effectiveFrom"] = ["Date hors limites."];
        }
        else if (await db.SavingsRates.AnyAsync(
            r => r.AccountId == account.Id && r.EffectiveFrom == request.EffectiveFrom && (existing == null || r.Id != existing.Id), ct))
        {
            errors["effectiveFrom"] = ["Un taux commence déjà à cette date."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static SavingsRatePeriod ToPeriod(SavingsRate r) => new(r.EffectiveFrom, r.AnnualRate);

    private static SavingsRateDto ToDto(SavingsRate r, DateOnly today) =>
        new(r.Id, r.AccountId, r.EffectiveFrom, r.AnnualRate, r.EffectiveFrom > today, r.Version);
}
