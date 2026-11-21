using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>Comptes du foyer (A1, A2, A3, A6).</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts");

        group.MapGet("/", ListAsync)
            .WithSummary("Comptes avec leur solde, et patrimoine net.");
        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Un compte avec son solde.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée un compte.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<AccountDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un compte (If-Match requis). L'archivage passe par isArchived.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un compte sans opération (If-Match requis) ; sinon, l'archiver.");

        return app;
    }

    public sealed record AccountRequest(
        [property: Required, StringLength(100, MinimumLength = 1)] string Name,
        AccountType Type,
        [property: Range(-999_999_999, 999_999_999)] decimal InitialBalance,
        DateOnly InitialDate,
        [property: StringLength(16)] string? Color,
        [property: StringLength(40)] string? Icon,
        bool? IncludeInForecast,
        bool? IncludeInNetWorth,
        [property: Range(-999_999_999, 999_999_999)] decimal? OverdraftThreshold,
        bool? IsArchived,
        int? SortOrder);

    public sealed record AccountDto(
        Guid Id,
        string Name,
        AccountType Type,
        decimal InitialBalance,
        DateOnly InitialDate,
        string? Color,
        string? Icon,
        bool IncludeInForecast,
        bool IncludeInNetWorth,
        decimal OverdraftThreshold,
        bool IsArchived,
        int SortOrder,
        decimal Balance,
        decimal FutureBalance,
        int Version);

    public sealed record AccountList(IReadOnlyList<AccountDto> Items, decimal NetWorth);

    private static async Task<Ok<AccountList>> ListAsync(AppDbContext db, TimeProvider clock, bool? includeArchived)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => includeArchived == true || !a.IsArchived)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync();
        var balances = await Balances.ComputeAsync(db, clock, accounts.Select(a => a.Id));
        var items = accounts.Select(a => ToDto(a, balances)).ToList();
        var netWorth = items.Where(a => a.IncludeInNetWorth && !a.IsArchived).Sum(a => a.Balance);
        return TypedResults.Ok(new AccountList(items, netWorth));
    }

    private static async Task<Results<Ok<AccountDto>, NotFound>> GetAsync(Guid id, AppDbContext db, TimeProvider clock)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(ToDto(account, await Balances.ComputeAsync(db, clock, [id])));
    }

    private static async Task<Results<Created<AccountDto>, ValidationProblem>> CreateAsync(
        AccountRequest request, AppDbContext db, TimeProvider clock)
    {
        if (await ValidateAsync(request, db, null) is { } problem)
        {
            return problem;
        }
        var account = new Account { Name = request.Name.Trim() };
        Apply(account, request);
        account.IncludeInForecast = request.IncludeInForecast ?? request.Type == AccountType.Checking;
        account.SortOrder = request.SortOrder ?? (await db.Accounts.MaxAsync(a => (int?)a.SortOrder) ?? -1) + 1;
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/accounts/{account.Id}", ToDto(account, await Balances.ComputeAsync(db, clock, [account.Id])));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, AccountRequest request, HttpRequest http, AppDbContext db, TimeProvider clock)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, account) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, account) is { } problem)
        {
            return problem;
        }
        account.Name = request.Name.Trim();
        Apply(account, request);
        account.IncludeInForecast = request.IncludeInForecast ?? account.IncludeInForecast;
        account.SortOrder = request.SortOrder ?? account.SortOrder;
        if (!await db.TrySaveAsync())
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok(ToDto(account, await Balances.ComputeAsync(db, clock, [id])));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, account) is { } precondition)
        {
            return precondition;
        }
        if (await db.Transactions.AnyAsync(t => t.AccountId == id))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ce compte contient des opérations.",
                detail: "Archivez-le plutôt : il sera masqué et son historique conservé.");
        }
        db.Accounts.Remove(account);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static void Apply(Account account, AccountRequest request)
    {
        account.Type = request.Type;
        account.InitialBalance = request.InitialBalance;
        account.InitialDate = request.InitialDate;
        account.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        account.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        account.IncludeInNetWorth = request.IncludeInNetWorth ?? account.IncludeInNetWorth;
        account.OverdraftThreshold = request.OverdraftThreshold ?? account.OverdraftThreshold;
        account.IsArchived = request.IsArchived ?? account.IsArchived;
    }

    private static async Task<ValidationProblem?> ValidateAsync(AccountRequest request, AppDbContext db, Account? existing)
    {
        var errors = new Dictionary<string, string[]>();
        if (decimal.Round(request.InitialBalance, 2) != request.InitialBalance)
        {
            errors["initialBalance"] = ["Le montant a au plus 2 décimales."];
        }
        var name = request.Name.Trim();
        var names = await db.Accounts.Where(a => existing == null || a.Id != existing.Id).Select(a => a.Name).ToListAsync();
        if (names.Any(n => string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            errors["name"] = ["Un compte porte déjà ce nom."];
        }
        if (existing is not null && request.InitialDate > existing.InitialDate
            && await db.Transactions.AnyAsync(t => t.AccountId == existing.Id && t.Date < request.InitialDate))
        {
            errors["initialDate"] = ["Des opérations sont antérieures à cette date."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static AccountDto ToDto(Account a, IReadOnlyDictionary<Guid, Balances.AccountBalance> balances)
    {
        var balance = balances.GetValueOrDefault(a.Id);
        return new AccountDto(
            a.Id, a.Name, a.Type, a.InitialBalance, a.InitialDate, a.Color, a.Icon, a.IncludeInForecast,
            a.IncludeInNetWorth, a.OverdraftThreshold, a.IsArchived, a.SortOrder,
            a.InitialBalance + balance.UntilToday, a.InitialBalance + balance.Total, a.Version);
    }
}
