using System.Text;

using HomeBudget.Api.Domain.Tags;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Features.Payees;
using HomeBudget.Api.Features.Reconciliations;
using HomeBudget.Api.Features.Rules;
using HomeBudget.Api.Features.Tags;
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
            .Produces<TransactionDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Pointe ou dépointe une opération. Une opération rapprochée ne se dépointe pas.");
        group.MapGet("/{id:guid}/similar", SimilarAsync)
            .WithSummary("Opérations de même libellé bancaire dont le tiers ou la catégorie diffère : candidates à la même correction.");
        group.MapPut("/{id:guid}/links", SetLinksAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<TransactionDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Lie un revenu à la dépense qu'il rembourse (B11) et à l'avance qu'il solde (B12) ; null défait le lien.");
        group.MapGet("/{id:guid}/reimbursable", ReimbursableAsync)
            .WithSummary("Dépenses que ce revenu peut rembourser : celles de l'année précédente, filtrées par le texte recherché.");
        group.MapPost("/bulk", BulkAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Modifie plusieurs opérations en une fois (catégorie, tiers, compte, pointage, tags), dans une seule transaction.");

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
        Guid? tagId,
        string? cursor,
        int? limit)
    {
        var query = await new TransactionFilter(accountId, categoryId, payeeId, from, to, minAmount, maxAmount, search, cleared, tagId)
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
            .Include(t => t.Lines).ThenInclude(l => l.Tags)
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
        var transaction = await db.Transactions.AsNoTracking().Include(t => t.Lines).ThenInclude(l => l.Tags).FirstOrDefaultAsync(t => t.Id == id);
        return transaction is null ? TypedResults.NotFound() : TypedResults.Ok((await ToDtosAsync(db, [transaction]))[0]);
    }

    private static async Task<Results<Ok<List<TransactionDto>>, NotFound>> SimilarAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(await ToDtosAsync(db, await LabelRules.SimilarAsync(db, transaction, ct)));
    }

    private static async Task<Results<Ok<BulkUpdateResult>, ValidationProblem>> BulkAsync(
        BulkUpdateRequest request, AppDbContext db, CancellationToken ct)
    {
        IReadOnlyList<string> addTags = request.AddTags ?? [];
        IReadOnlyList<string> removeTags = request.RemoveTags ?? [];
        var errors = new Dictionary<string, string[]>();
        if (request.Category is null && string.IsNullOrWhiteSpace(request.PayeeName) && request.AccountId is null
            && request.IsCleared is null && addTags.Count == 0 && removeTags.Count == 0)
        {
            errors["ids"] = ["Aucune modification demandée."];
        }
        if (request.Category?.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            errors["category.categoryId"] = ["Catégorie introuvable."];
        }
        Domain.Accounts.Account? account = null;
        if (request.AccountId is { } accountId)
        {
            account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null)
            {
                errors["accountId"] = ["Compte introuvable."];
            }
            else if (account.IsArchived)
            {
                errors["accountId"] = ["Ce compte est archivé."];
            }
        }
        if (TagEndpoints.Validate(addTags) is { } addError)
        {
            errors["addTags"] = [addError];
        }
        if (TagEndpoints.Validate(removeTags) is { } removeError)
        {
            errors["removeTags"] = [removeError];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var ids = request.Ids.Distinct().ToList();
        var transactions = await db.Transactions.Include(t => t.Lines).ThenInclude(l => l.Tags)
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(ct);
        var removed = removeTags.Select(Tag.Normalize).ToHashSet(StringComparer.Ordinal);
        var payeeRequested = !string.IsNullOrWhiteSpace(request.PayeeName);
        var updated = new List<Transaction>();
        foreach (var t in transactions)
        {
            // Une modification qui ne s'applique pas laisse l'opération intacte, plutôt qu'à moitié modifiée.
            var isTransfer = t.TransferId is not null;
            if (t.IsReconciled
                || (isTransfer && (request.Category is not null || payeeRequested || account is not null))
                || (request.Category is not null && t.Lines.Count != 1)
                || (account is not null && account.Id != t.AccountId && (t.ImportHash is not null || t.Date < account.InitialDate)))
            {
                continue;
            }
            updated.Add(t);
        }
        if (updated.Count == 0)
        {
            return TypedResults.Ok(new BulkUpdateResult(0, ids.Count));
        }

        var payee = payeeRequested ? await PayeeEndpoints.FindOrCreateAsync(db, request.PayeeName) : null;
        var added = await TagEndpoints.FindOrCreateAsync(db, addTags, ct);
        foreach (var t in updated)
        {
            if (request.Category is { } category)
            {
                t.Lines[0].CategoryId = category.CategoryId;
            }
            if (payee is not null)
            {
                t.PayeeId = payee.Id;
            }
            if (account is not null)
            {
                t.AccountId = account.Id;
            }
            if (request.IsCleared is { } isCleared)
            {
                t.IsCleared = isCleared;
            }
            foreach (var line in t.Lines)
            {
                line.Tags.RemoveAll(g => removed.Contains(g.NormalizedName));
                line.Tags.AddRange(added.Where(tag => !line.Tags.Exists(g => g.Id == tag.Id)));
            }
            if (request.Category is not null || payee is not null)
            {
                await LabelRules.LearnAsync(db, t, ct);
            }
            // Une modification des seules lignes compte comme une modification de l'opération (auteur, version).
            if (db.Entry(t).State == EntityState.Unchanged)
            {
                db.Entry(t).State = EntityState.Modified;
            }
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new BulkUpdateResult(updated.Count, ids.Count - updated.Count));
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
        if (transaction.IsReconciled)
        {
            return ReconciliationLock.Problem();
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
        // Corriger une opération importée apprend la règle de son libellé bancaire (I2, I3).
        await LabelRules.LearnAsync(db, transaction);
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
        if (transaction.IsReconciled)
        {
            return ReconciliationLock.Problem();
        }
        if (Concurrency.Check(http, transaction) is { } precondition)
        {
            return precondition;
        }
        db.Transactions.Remove(transaction);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<IResult> ClearAsync(Guid id, ClearRequest request, AppDbContext db)
    {
        var transaction = await db.Transactions.Include(t => t.Lines).ThenInclude(l => l.Tags).FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        if (transaction.IsReconciled && !request.IsCleared)
        {
            return ReconciliationLock.Problem();
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
            if (TagEndpoints.Validate(line.Tags) is { } tagError)
            {
                errors[$"lines[{i}].tags"] = [tagError];
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
        var lines = new List<TransactionLine>(request.Lines.Count);
        foreach (var l in request.Lines)
        {
            lines.Add(new TransactionLine
            {
                CategoryId = l.CategoryId,
                Amount = l.Amount,
                Memo = Clean(l.Memo),
                Tags = await TagEndpoints.FindOrCreateAsync(db, l.Tags),
            });
        }
        transaction.SetLines(lines);
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

        var expenseIds = transactions.Where(t => t.Amount < 0).Select(t => t.Id).ToList();
        var reimbursed = (await db.Transactions.AsNoTracking()
                .Where(t => t.ReimbursesId != null && expenseIds.Contains(t.ReimbursesId.Value))
                .Select(t => new { ExpenseId = t.ReimbursesId!.Value, t.Amount })
                .ToListAsync())
            .GroupBy(x => x.ExpenseId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

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
            [.. t.Lines.OrderBy(l => l.SortOrder).Select(l => new TransactionLineDto(
                l.Id, l.CategoryId, l.Amount, l.Memo, [.. l.Tags.Select(g => g.Name).Order(StringComparer.CurrentCultureIgnoreCase)]))],
            t.CreatedAt,
            t.UpdatedAt,
            t.Version,
            t.IsReconciled,
            t.ReimbursesId,
            reimbursed.GetValueOrDefault(t.Id),
            t.AdvanceId,
            t.CreatedBy,
            t.UpdatedBy))];
    }

    private static async Task<IResult> SetLinksAsync(Guid id, TransactionLinksRequest request, AppDbContext db, CancellationToken ct)
    {
        var transaction = await db.Transactions.Include(t => t.Lines).ThenInclude(l => l.Tags).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }
        var errors = new Dictionary<string, string[]>();
        var linking = request.ReimbursesId is not null || request.AdvanceId is not null;
        if (linking && (transaction.Amount <= 0 || transaction.TransferId is not null))
        {
            errors["id"] = ["Seul un revenu (hors virement) peut rembourser une dépense ou solder une avance."];
        }
        if (request.ReimbursesId is { } expenseId)
        {
            var expense = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == expenseId, ct);
            if (expense is null)
            {
                errors["reimbursesId"] = ["Dépense introuvable."];
            }
            else if (expense.Amount >= 0 || expense.TransferId is not null)
            {
                errors["reimbursesId"] = ["Choisissez une dépense (hors virement)."];
            }
        }
        if (request.AdvanceId is { } advanceId && !await db.Advances.AnyAsync(a => a.Id == advanceId, ct))
        {
            errors["advanceId"] = ["Avance introuvable."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }
        transaction.ReimbursesId = request.ReimbursesId;
        transaction.AdvanceId = request.AdvanceId;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ToDtosAsync(db, [transaction]))[0]);
    }

    /// <summary>Dépenses que ce revenu peut rembourser (B11) : celles de l'année qui précède, les plus récentes d'abord.</summary>
    private static async Task<Results<Ok<List<TransactionDto>>, NotFound>> ReimbursableAsync(
        Guid id, string? search, AppDbContext db, CancellationToken ct)
    {
        var income = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (income is null)
        {
            return TypedResults.NotFound();
        }
        var query = await new TransactionFilter(null, null, null, income.Date.AddYears(-1), income.Date.AddDays(1), null, null, search, null)
            .ApplyAsync(db, db.Transactions.AsNoTracking(), ct);
        var expenses = await query
            .Where(t => t.Amount < 0 && t.TransferId == null)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Include(t => t.Lines).ThenInclude(l => l.Tags)
            .Take(20)
            .ToListAsync(ct);
        return TypedResults.Ok(await ToDtosAsync(db, expenses));
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
