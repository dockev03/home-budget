using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>
/// Crédits (A8) : conditions d'un compte de type crédit (capital, taux, durée, assurance), tableau d'amortissement
/// et capital restant dû. Elles suivent la visibilité de leur compte (R8).
/// </summary>
public static class LoanEndpoints
{
    private const decimal MaxAmount = 999_999_999m;

    public static IEndpointRouteBuilder MapLoanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}/loan").WithTags("Accounts");

        group.MapGet("/", GetAsync)
            .Produces<LoanDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Conditions du crédit, synthèse et tableau d'amortissement (404 si le compte n'en a pas).");
        group.MapPut("/", SaveAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<LoanDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Saisit ou modifie les conditions du crédit d'un compte de type crédit (If-Match requis pour une modification).");
        group.MapDelete("/", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime les conditions du crédit (If-Match requis).");

        return app;
    }

    /// <param name="Principal">Capital emprunté.</param>
    /// <param name="AnnualRate">Taux nominal annuel en pourcentage (4 décimales au plus).</param>
    /// <param name="DurationMonths">Nombre de mensualités (1 à 600).</param>
    /// <param name="FirstPaymentDate">Date de la première mensualité.</param>
    /// <param name="MonthlyInsurance">Assurance emprunteur par mensualité (0 par défaut).</param>
    public sealed record LoanRequest(
        decimal Principal, decimal AnnualRate, int DurationMonths, DateOnly FirstPaymentDate, decimal MonthlyInsurance = 0);

    /// <param name="Payment">Mensualité hors assurance.</param>
    /// <param name="Remaining">Capital restant dû après la mensualité.</param>
    public sealed record LoanInstallmentDto(
        int Number, DateOnly Date, decimal Payment, decimal Interest, decimal Principal, decimal Insurance, decimal Remaining);

    /// <param name="Payment">Mensualité hors assurance.</param>
    /// <param name="PaymentWithInsurance">Mensualité assurance comprise.</param>
    /// <param name="TotalCost">Coût total du crédit : intérêts + assurance.</param>
    /// <param name="RemainingPrincipal">Capital restant dû aujourd'hui.</param>
    /// <param name="PaidCount">Mensualités échues à ce jour.</param>
    public sealed record LoanDto(
        Guid AccountId,
        decimal Principal,
        decimal AnnualRate,
        int DurationMonths,
        DateOnly FirstPaymentDate,
        decimal MonthlyInsurance,
        decimal Payment,
        decimal PaymentWithInsurance,
        decimal TotalInterest,
        decimal TotalInsurance,
        decimal TotalCost,
        DateOnly EndDate,
        decimal RemainingPrincipal,
        int PaidCount,
        List<LoanInstallmentDto> Schedule,
        int Version);

    /// <summary>Capital restant dû aujourd'hui, par compte de crédit qui a des conditions saisies.</summary>
    public static async Task<Dictionary<Guid, decimal>> RemainingPrincipalsAsync(
        AppDbContext db, TimeProvider clock, IEnumerable<Guid> accountIds, CancellationToken ct = default)
    {
        var ids = accountIds.ToList();
        var today = Balances.Today(clock);
        var loans = await db.Loans.AsNoTracking().Where(l => ids.Contains(l.AccountId)).ToListAsync(ct);
        return loans.ToDictionary(l => l.AccountId, l => LoanSchedule.For(l).RemainingAt(today));
    }

    private static async Task<IResult> GetAsync(Guid accountId, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var loan = await db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.AccountId == accountId, ct);
        return loan is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(loan, clock));
    }

    private static async Task<IResult> SaveAsync(
        Guid accountId, LoanRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.AccountId == accountId, ct);
        if (loan is not null && Concurrency.Check(http, loan) is { } precondition)
        {
            return precondition;
        }
        if (Validate(request, account) is { } problem)
        {
            return problem;
        }
        if (loan is null)
        {
            loan = new Loan { AccountId = accountId };
            db.Loans.Add(loan);
        }
        loan.Principal = request.Principal;
        loan.AnnualRate = request.AnnualRate;
        loan.DurationMonths = request.DurationMonths;
        loan.FirstPaymentDate = request.FirstPaymentDate;
        loan.MonthlyInsurance = request.MonthlyInsurance;
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(ToDto(loan, clock)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid accountId, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.AccountId == accountId, ct);
        if (loan is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, loan) is { } precondition)
        {
            return precondition;
        }
        db.Loans.Remove(loan);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static IResult? Validate(LoanRequest request, Account account)
    {
        var errors = new Dictionary<string, string[]>();
        if (account.Type != AccountType.Credit)
        {
            errors["accountId"] = ["Seuls les comptes de crédit ont des conditions de crédit."];
        }
        if (request.Principal <= 0 || request.Principal > MaxAmount || !TransactionValidation.HasCents(request.Principal))
        {
            errors["principal"] = ["Le capital est positif, avec 2 décimales au plus."];
        }
        if (request.AnnualRate is < 0 or > 100 || decimal.Round(request.AnnualRate, Loan.RateDecimals) != request.AnnualRate)
        {
            errors["annualRate"] = [$"Le taux va de 0 à 100 %, avec {Loan.RateDecimals} décimales au plus."];
        }
        if (request.DurationMonths is < 1 or > Loan.MaxDurationMonths)
        {
            errors["durationMonths"] = [$"La durée va de 1 à {Loan.MaxDurationMonths} mois."];
        }
        if (request.MonthlyInsurance < 0 || request.MonthlyInsurance > MaxAmount || !TransactionValidation.HasCents(request.MonthlyInsurance))
        {
            errors["monthlyInsurance"] = ["L'assurance est positive ou nulle, avec 2 décimales au plus."];
        }
        if (request.FirstPaymentDate < TransactionValidation.MinDate
            || request.FirstPaymentDate.AddMonths(Math.Clamp(request.DurationMonths, 1, Loan.MaxDurationMonths)) > TransactionValidation.MaxDate)
        {
            errors["firstPaymentDate"] = ["Date de première mensualité hors limites."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static LoanDto ToDto(Loan loan, TimeProvider clock)
    {
        var schedule = LoanSchedule.For(loan);
        var today = Balances.Today(clock);
        return new LoanDto(
            loan.AccountId, loan.Principal, loan.AnnualRate, loan.DurationMonths, loan.FirstPaymentDate, loan.MonthlyInsurance,
            schedule.Payment, schedule.Payment + loan.MonthlyInsurance, schedule.TotalInterest, schedule.TotalInsurance,
            schedule.TotalInterest + schedule.TotalInsurance, schedule.EndDate, schedule.RemainingAt(today), schedule.PaidCountAt(today),
            [.. schedule.Installments.Select(i => new LoanInstallmentDto(i.Number, i.Date, i.Payment, i.Interest, i.Principal, i.Insurance, i.Remaining))],
            loan.Version);
    }
}
