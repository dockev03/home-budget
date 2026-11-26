using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Accounts;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>Comptes du foyer (A1, A2, A3, A6) et épargne de précaution (J3).</summary>
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
        int? SortOrder,
        bool? IsPrivate = null,
        bool? IsEmergencyFund = null);

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
        int Version,
        bool IsPrivate,
        Guid? OwnerId,
        bool IsEmergencyFund);

    public sealed record AccountList(IReadOnlyList<AccountDto> Items, decimal NetWorth);

    private static async Task<Ok<AccountList>> ListAsync(AppDbContext db, TimeProvider clock, bool? includeArchived)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => includeArchived == true || !a.IsArchived)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync();
        var balances = await Balances.ComputeAsync(db, clock, accounts);
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
        return TypedResults.Ok(ToDto(account, await Balances.ComputeAsync(db, clock, [account])));
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
        account.IsEmergencyFund = request.IsEmergencyFund ?? request.Type == AccountType.Savings;
        account.SortOrder = request.SortOrder ?? (await db.Accounts.MaxAsync(a => (int?)a.SortOrder) ?? -1) + 1;
        if (request.IsPrivate == true)
        {
            account.Visibility = AccountVisibility.Private;
            account.OwnerId = db.ViewerId;
        }
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/accounts/{account.Id}", ToDto(account, await Balances.ComputeAsync(db, clock, [account])));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, AccountRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, ClaimsPrincipal principal, AuditTrail audit)
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
        var isPrivate = account.Visibility == AccountVisibility.Private;
        if (request.IsPrivate is { } wanted && wanted != isPrivate)
        {
            // Rendre un compte partagé privé le retire aux autres membres : réservé à son auteur et aux administrateurs.
            if (wanted && account.CreatedBy != db.ViewerId && !principal.IsInRole(Roles.Admin))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["isPrivate"] = ["Seul l'auteur du compte ou un administrateur peut le rendre privé."],
                });
            }
            account.Visibility = wanted ? AccountVisibility.Private : AccountVisibility.Shared;
            account.OwnerId = wanted ? db.ViewerId : null;
            audit.Add(AuditAction.AccountVisibilityChanged, "Account", account.Id, wanted ? "Compte rendu privé." : "Compte partagé.");
        }
        account.Name = request.Name.Trim();
        Apply(account, request);
        account.IncludeInForecast = request.IncludeInForecast ?? account.IncludeInForecast;
        account.IsEmergencyFund = request.IsEmergencyFund ?? account.IsEmergencyFund;
        account.SortOrder = request.SortOrder ?? account.SortOrder;
        if (!await db.TrySaveAsync())
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok(ToDto(account, await Balances.ComputeAsync(db, clock, [account])));
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
        if (await db.Recurrences.IgnoreQueryFilters().AnyAsync(r => r.AccountId == id || r.ToAccountId == id))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ce compte a des récurrences.",
                detail: "Supprimez d'abord ses récurrences, ou archivez le compte.");
        }
        if (await db.SavingsGoals.IgnoreQueryFilters().AnyAsync(g => g.AccountId == id))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ce compte est rattaché à un objectif d'épargne.",
                detail: "Supprimez d'abord l'objectif, ou archivez le compte.");
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
        if (existing is not null && existing.Type == AccountType.Investment && request.Type != AccountType.Investment
            && await db.AccountValuations.AnyAsync(v => v.AccountId == existing.Id))
        {
            errors["type"] = ["Ce compte a des valorisations : supprimez-les avant de changer son type."];
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
            balance.Current, balance.Future, a.Version,
            a.Visibility == AccountVisibility.Private, a.OwnerId, a.IsEmergencyFund);
    }
}
