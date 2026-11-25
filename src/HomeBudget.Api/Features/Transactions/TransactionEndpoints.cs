using System.Text;

using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Features.Payees;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Transactions;

/// <summary>Opérations : saisie avec ventilation (B1, B4, B5, B6, B8), recherche (B3), pointage (B2).</summary>
public static class TransactionEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").WithTags("Transactions");

        group.MapGet("/", SearchAsync)
            .WithSummary("Recherche paginée (curseur), de la plus récente à la plus ancienne.");
        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Une opération.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée une opération (une ou plusieurs lignes de ventilation). Le tiers est créé s'il n'existe pas.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<TransactionDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une opération (If-Match requis). Un virement se modifie via /api/transfers.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une opération (If-Match requis). Un virement se supprime via /api/transfers.");
        group.MapPost("/{id:guid}/clear", ClearAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Pointe ou dépointe une opération.");

        return app;
    }

    private static async Task<Results<Ok<TransactionPage>, ValidationProblem>> SearchAsync(
        AppDbContext db,
        Guid? accountId,
        Guid? categoryId,
        Guid? payeeId,
        DateOnly? from,
        DateOnly? to,
        decimal? minAmount,
        decimal? maxAmount,
        string? search,
        bool? cleared,
        string? cursor,
        int? limit)
    {
        var query = await new TransactionFilter(accountId, categoryId, payeeId, from, to, minAmount, maxAmount, search, cleared)
            .ApplyAsync(db, db.Transactions.AsNoTracking());
        if (cursor is not null)
        {
            if (!TryDecodeCursor(cursor, out var cursorDate, out var cursorId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Curseur invalide."] });
            }
            query = query.Where(t => t.Date < cursorDate || (t.Date == cursorDate && t.Id.CompareTo(cursorId) < 0));
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var page = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Include(t => t.Lines)
            .Take(size + 1)
            .ToListAsync();
        var hasMore = page.Count > size;
        if (hasMore)
        {
            page.RemoveAt(size);
        }
        var items = await ToDtosAsync(db, page);
        return TypedResults.Ok(new TransactionPage(items, hasMore ? EncodeCursor(page[^1]) : null));
    }

    private static async Task<Results<Ok<TransactionDto>, NotFound>> GetAsync(Guid id, AppDbContext db)
    {
        var transaction = await db.Transactions.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id);
        return transaction is null ? TypedResults.NotFound() : TypedResults.Ok((await ToDtosAsync(db, [transaction]))[0]);
    }

    private static async Task<Results<Created<TransactionDto>, ValidationProblem>> CreateAsync(TransactionRequest request, AppDbContext db)
    {
        if (await ValidateAsync(request, db, null) is { } problem)
        {
            return problem;
        }
        var transaction = new Transaction();
        await ApplyAsync(transaction, request, db);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/transactions/{transaction.Id}", (await ToDtosAsync(db, [transaction]))[0]);
    }

    private static async Task<IResult> UpdateAsync(Guid id, TransactionRequest request, HttpRequest http, AppDbContext db)
    {
        var transaction = await db.Transactions.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        if (transaction.TransferId is not null)
        {
            return TransferOnly();
        }
        if (Concurrency.Check(http, transaction) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, transaction) is { } problem)
        {
            return problem;
        }
        await ApplyAsync(transaction, request, db);
        if (!await db.TrySaveAsync())
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok((await ToDtosAsync(db, [transaction]))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        if (transaction.TransferId is not null)
        {
            return TransferOnly();
        }
        if (Concurrency.Check(http, transaction) is { } precondition)
        {
            return precondition;
        }
        db.Transactions.Remove(transaction);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<Results<Ok<TransactionDto>, NotFound>> ClearAsync(Guid id, ClearRequest request, AppDbContext db)
    {
        var transaction = await db.Transactions.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        if (transaction.IsCleared != request.IsCleared)
        {
            transaction.IsCleared = request.IsCleared;
            await db.SaveChangesAsync();
        }
        return TypedResults.Ok((await ToDtosAsync(db, [transaction]))[0]);
    }

    private static async Task<ValidationProblem?> ValidateAsync(TransactionRequest request, AppDbContext db, Transaction? existing)
    {
        var errors = new Dictionary<string, string[]>();
        await TransactionValidation.CheckAccountAsync(db, request.AccountId, request.Date, existing?.AccountId, "accountId", errors);

        var categories = await TransactionValidation.LoadCategoriesAsync(db, request.Lines.Select(l => l.CategoryId));
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            if (line.Amount == 0)
            {
                errors[$"lines[{i}].amount"] = ["Le montant ne peut pas être nul."];
            }
            else if (!TransactionValidation.HasCents(line.Amount))
            {
                errors[$"lines[{i}].amount"] = ["Le montant a au plus 2 décimales."];
            }
            if (line.CategoryId is { } categoryId && !categories.ContainsKey(categoryId))
            {
                errors[$"lines[{i}].categoryId"] = ["Catégorie introuvable."];
            }
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static async Task ApplyAsync(Transaction transaction, TransactionRequest request, AppDbContext db)
    {
        transaction.AccountId = request.AccountId;
        transaction.Date = request.Date;
        transaction.PayeeId = (await PayeeEndpoints.FindOrCreateAsync(db, request.PayeeName))?.Id;
        transaction.Label = Clean(request.Label);
        transaction.Notes = Clean(request.Notes);
        transaction.IsCleared = request.IsCleared ?? transaction.IsCleared;
        transaction.SetLines(request.Lines.Select(l => new TransactionLine
        {
            CategoryId = l.CategoryId,
            Amount = l.Amount,
            Memo = Clean(l.Memo),
        }));
    }

    internal static async Task<List<TransactionDto>> ToDtosAsync(AppDbContext db, IReadOnlyList<Transaction> transactions)
    {
        var payeeIds = transactions.Select(t => t.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);

        var transferIds = transactions.Select(t => t.TransferId).OfType<Guid>().Distinct().ToList();
        var counterparts = await db.Transactions.AsNoTracking()
            .Where(t => t.TransferId != null && transferIds.Contains(t.TransferId.Value))
            .Select(t => new { t.Id, t.TransferId, t.AccountId })
            .ToListAsync();

        return [.. transactions.Select(t => new TransactionDto(
            t.Id,
            t.AccountId,
            t.Date,
            t.PayeeId,
            t.PayeeId is { } p ? payees.GetValueOrDefault(p) : null,
            t.Label,
            t.Notes,
            t.Amount,
            t.IsCleared,
            t.TransferId,
            counterparts.FirstOrDefault(c => c.TransferId == t.TransferId && c.Id != t.Id)?.AccountId,
            [.. t.Lines.OrderBy(l => l.SortOrder).Select(l => new TransactionLineDto(l.Id, l.CategoryId, l.Amount, l.Memo))],
            t.CreatedAt,
            t.UpdatedAt,
            t.Version))];
    }

    private static IResult TransferOnly() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Cette opération fait partie d'un virement.",
        detail: "Modifiez ou supprimez le virement lui-même.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EncodeCursor(Transaction last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.Date:yyyy-MM-dd}|{last.Id}"));

    private static bool TryDecodeCursor(string cursor, out DateOnly date, out Guid id)
    {
        date = default;
        id = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2
                && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)
                && Guid.TryParse(parts[1], out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
