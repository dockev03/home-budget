using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Transactions;

/// <summary>
/// Virements entre comptes du foyer (A4, R5) : une saisie, deux opérations liées par un même identifiant,
/// exclues des revenus et des dépenses. Une catégorie de type épargne peut être portée par la sortie.
/// </summary>
public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transfers").WithTags("Transfers");

        group.MapGet("/{transferId:guid}", GetAsync)
            .WithSummary("Un virement.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée un virement (deux opérations liées).");
        group.MapPut("/{transferId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<TransferDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un virement (If-Match requis : version du virement).");
        group.MapDelete("/{transferId:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un virement et ses deux opérations (If-Match requis).");

        return app;
    }

    private static async Task<Results<Ok<TransferDto>, NotFound>> GetAsync(Guid transferId, AppDbContext db)
    {
        var legs = await LoadAsync(db, transferId, tracking: false);
        return legs is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(legs.Value.From, legs.Value.To));
    }

    private static async Task<Results<Created<TransferDto>, ValidationProblem>> CreateAsync(TransferRequest request, AppDbContext db)
    {
        if (await ValidateAsync(request, db, null) is { } problem)
        {
            return problem;
        }
        var transferId = Guid.CreateVersion7();
        var from = new Transaction { TransferId = transferId };
        var to = new Transaction { TransferId = transferId };
        Apply(from, to, request);
        db.Transactions.AddRange(from, to);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/transfers/{transferId}", ToDto(from, to));
    }

    private static async Task<IResult> UpdateAsync(Guid transferId, TransferRequest request, HttpRequest http, AppDbContext db)
    {
        if (await LoadAsync(db, transferId, tracking: true) is not var (from, to))
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, from) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, (from, to)) is { } problem)
        {
            return problem;
        }
        Apply(from, to, request);
        return await db.TrySaveAsync() ? TypedResults.Ok(ToDto(from, to)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid transferId, HttpRequest http, AppDbContext db)
    {
        if (await LoadAsync(db, transferId, tracking: true) is not var (from, to))
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, from) is { } precondition)
        {
            return precondition;
        }
        db.Transactions.RemoveRange(from, to);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    /// <summary>Les deux opérations d'un virement : la sortie (montant négatif) puis l'entrée.</summary>
    private static async Task<(Transaction From, Transaction To)?> LoadAsync(AppDbContext db, Guid transferId, bool tracking)
    {
        var query = db.Transactions.Include(t => t.Lines).Where(t => t.TransferId == transferId);
        var legs = await (tracking ? query : query.AsNoTracking()).ToListAsync();
        if (legs.Count != 2)
        {
            return null;
        }
        var from = legs.First(t => t.Amount < 0);
        return (from, legs.First(t => t != from));
    }

    private static void Apply(Transaction from, Transaction to, TransferRequest request)
    {
        foreach (var (leg, accountId, sign) in new[] { (from, request.FromAccountId, -1m), (to, request.ToAccountId, 1m) })
        {
            leg.AccountId = accountId;
            leg.Date = request.Date;
            leg.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
            leg.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            leg.PayeeId = null;
            leg.SetLines([new TransactionLine
            {
                Amount = sign * request.Amount,
                // Seule la sortie porte la catégorie d'épargne (R5) : l'épargne n'est comptée qu'une fois.
                CategoryId = sign < 0 ? request.CategoryId : null,
            }]);
        }
    }

    private static async Task<ValidationProblem?> ValidateAsync(
        TransferRequest request, AppDbContext db, (Transaction From, Transaction To)? existing)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.FromAccountId == request.ToAccountId)
        {
            errors["toAccountId"] = ["Choisissez deux comptes différents."];
        }
        if (!TransactionValidation.HasCents(request.Amount))
        {
            errors["amount"] = ["Le montant a au plus 2 décimales."];
        }
        await TransactionValidation.CheckAccountAsync(db, request.FromAccountId, request.Date, existing?.From.AccountId, "fromAccountId", errors);
        await TransactionValidation.CheckAccountAsync(db, request.ToAccountId, request.Date, existing?.To.AccountId, "toAccountId", errors);
        if (request.CategoryId is { } categoryId)
        {
            var categories = await TransactionValidation.LoadCategoriesAsync(db, [categoryId]);
            if (!categories.TryGetValue(categoryId, out var category))
            {
                errors["categoryId"] = ["Catégorie introuvable."];
            }
            else if (category.Kind != CategoryKind.Savings)
            {
                errors["categoryId"] = ["Un virement ne peut porter qu'une catégorie de type épargne."];
            }
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static TransferDto ToDto(Transaction from, Transaction to) => new(
        from.TransferId!.Value,
        from.Id,
        to.Id,
        from.AccountId,
        to.AccountId,
        from.Date,
        -from.Amount,
        from.Label,
        from.Notes,
        from.Lines.FirstOrDefault()?.CategoryId,
        from.Version);
}
