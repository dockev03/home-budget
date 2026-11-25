using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Transactions;

/// <summary>Filtres de la recherche d'opérations (B3), partagés avec l'export (H5).</summary>
public sealed record TransactionFilter(
    Guid? AccountId,
    Guid? CategoryId,
    Guid? PayeeId,
    DateOnly? From,
    DateOnly? To,
    decimal? MinAmount,
    decimal? MaxAmount,
    string? Search,
    bool? Cleared)
{
    public async Task<IQueryable<Transaction>> ApplyAsync(AppDbContext db, IQueryable<Transaction> query, CancellationToken ct = default)
    {
        if (AccountId is { } account)
        {
            query = query.Where(t => t.AccountId == account);
        }
        if (CategoryId is { } category)
        {
            // Une catégorie principale inclut ses sous-catégories.
            var ids = await db.Categories.Where(c => c.Id == category || c.ParentId == category).Select(c => c.Id).ToListAsync(ct);
            query = query.Where(t => t.Lines.Any(l => l.CategoryId != null && ids.Contains(l.CategoryId.Value)));
        }
        if (PayeeId is { } payee)
        {
            query = query.Where(t => t.PayeeId == payee);
        }
        if (From is { } start)
        {
            query = query.Where(t => t.Date >= start);
        }
        if (To is { } end)
        {
            query = query.Where(t => t.Date <= end);
        }
        // Les bornes de montant portent sur la valeur absolue : « 50 € minimum » vaut pour une dépense comme pour un revenu.
        if (MinAmount is { } min)
        {
            var bound = Math.Abs(min);
            query = query.Where(t => t.Amount >= bound || t.Amount <= -bound);
        }
        if (MaxAmount is { } max)
        {
            var bound = Math.Abs(max);
            query = query.Where(t => t.Amount <= bound && t.Amount >= -bound);
        }
        if (Cleared is { } isCleared)
        {
            query = query.Where(t => t.IsCleared == isCleared);
        }
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var pattern = "%" + EscapeLike(Search.Trim()) + "%";
            query = query.Where(t =>
                EF.Functions.Like(t.Label, pattern, "\\")
                || EF.Functions.Like(t.Notes, pattern, "\\")
                || db.Payees.Any(p => p.Id == t.PayeeId && EF.Functions.Like(p.Name, pattern, "\\"))
                || t.Lines.Any(l => EF.Functions.Like(l.Memo, pattern, "\\")));
        }
        return query;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
