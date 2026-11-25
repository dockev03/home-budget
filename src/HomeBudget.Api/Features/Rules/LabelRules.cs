using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Rules;

/// <summary>Tiers et catégorie proposés pour une opération.</summary>
public sealed record LabelSuggestion(Guid? PayeeId, string? PayeeName, Guid? CategoryId);

/// <summary>
/// Règles apprises (I2 à I4) : apprentissage à chaque correction d'une opération importée, suggestion d'après
/// les règles et l'historique du tiers, recherche des opérations semblables à corriger de la même façon.
/// </summary>
public static class LabelRules
{
    /// <summary>Nombre d'opérations récentes d'un tiers consultées pour proposer sa catégorie habituelle.</summary>
    public const int HistorySize = 20;

    /// <summary>Nombre maximal d'opérations semblables proposées à la correction.</summary>
    public const int MaxSimilar = 200;

    /// <summary>
    /// Met à jour la règle du libellé bancaire de l'opération d'après son tiers et sa catégorie (opération non
    /// ventilée). Sans effet sur une opération saisie à la main, un virement, ou un libellé sans mot significatif.
    /// </summary>
    public static async Task LearnAsync(AppDbContext db, Transaction transaction, CancellationToken ct = default)
    {
        if (transaction.TransferId is not null || string.IsNullOrEmpty(transaction.RawLabel))
        {
            return;
        }
        var key = LabelKey.From(transaction.RawLabel);
        var categoryId = transaction.Lines.Count == 1 ? transaction.Lines[0].CategoryId : null;
        if (key.Length == 0 || (transaction.PayeeId is null && categoryId is null))
        {
            return;
        }
        var rule = db.LabelRules.Local.FirstOrDefault(r => r.Key == key)
            ?? await db.LabelRules.FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rule is null)
        {
            rule = new LabelRule { Key = key };
            db.LabelRules.Add(rule);
        }
        // La dernière correction l'emporte ; une information absente ne vide pas la règle.
        if (transaction.PayeeId is { } payeeId)
        {
            rule.PayeeId = payeeId;
        }
        if (categoryId is { } category)
        {
            rule.CategoryId = category;
        }
    }

    /// <summary>Suggestion par clé de libellé, pour les libellés dont une règle existe.</summary>
    public static async Task<Dictionary<string, LabelSuggestion>> SuggestByLabelAsync(
        AppDbContext db, IEnumerable<string?> labels, CancellationToken ct = default)
    {
        var keys = labels.Select(LabelKey.From).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return [];
        }
        var rules = await db.LabelRules.AsNoTracking().Where(r => keys.Contains(r.Key)).ToListAsync(ct);
        var payeeIds = rules.Select(r => r.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var active = await ActiveCategoriesAsync(db, rules.Select(r => r.CategoryId), ct);
        var history = await CategoriesByPayeeAsync(
            db, [.. rules.Where(r => r.CategoryId is not { } c || !active.Contains(c)).Select(r => r.PayeeId).OfType<Guid>().Distinct()], ct);

        return rules.ToDictionary(
            r => r.Key,
            r => new LabelSuggestion(
                r.PayeeId,
                r.PayeeId is { } p ? payees.GetValueOrDefault(p) : null,
                r.CategoryId is { } c && active.Contains(c) ? c : r.PayeeId is { } p2 ? history.GetValueOrDefault(p2) : null),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Catégorie habituelle de chaque tiers : la plus fréquente parmi ses <see cref="HistorySize"/> dernières
    /// opérations non ventilées, la plus récente en cas d'égalité. Les catégories archivées sont ignorées.
    /// </summary>
    public static async Task<Dictionary<Guid, Guid?>> CategoriesByPayeeAsync(
        AppDbContext db, IReadOnlyCollection<Guid> payeeIds, CancellationToken ct = default)
    {
        if (payeeIds.Count == 0)
        {
            return [];
        }
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.PayeeId != null && payeeIds.Contains(t.PayeeId.Value) && t.TransferId == null && t.Lines.Count == 1)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => new { PayeeId = t.PayeeId!.Value, t.Lines.First().CategoryId })
            .ToListAsync(ct);
        var active = await ActiveCategoriesAsync(db, rows.Select(r => r.CategoryId), ct);

        return rows.GroupBy(r => r.PayeeId).ToDictionary(
            g => g.Key,
            g => g.Take(HistorySize)
                .Select((r, rank) => (r.CategoryId, rank))
                .Where(x => x.CategoryId is { } c && active.Contains(c))
                .GroupBy(x => x.CategoryId)
                .OrderByDescending(c => c.Count()).ThenBy(c => c.Min(x => x.rank))
                .Select(c => c.Key)
                .FirstOrDefault());
    }

    /// <summary>
    /// Opérations non ventilées de même clé de libellé que <paramref name="source"/>, dont le tiers ou la catégorie
    /// diffère du sien (I4) : candidates à la même correction, de la plus récente à la plus ancienne.
    /// </summary>
    public static async Task<List<Transaction>> SimilarAsync(AppDbContext db, Transaction source, CancellationToken ct = default)
    {
        var key = LabelKey.From(source.RawLabel ?? source.Label);
        var payeeId = source.PayeeId;
        var categoryId = source.Lines.Count == 1 ? source.Lines[0].CategoryId : null;
        if (key.Length == 0 || source.TransferId is not null || (payeeId is null && categoryId is null))
        {
            return [];
        }

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.Id != source.Id && t.TransferId == null && t.Lines.Count == 1);
        // Préfiltre SQL sur le mot le plus long de la clé (sans accent, donc retrouvé par LIKE quelle que soit la casse).
        var word = key.Split(' ').Where(w => w.All(char.IsAsciiLetter)).MaxBy(w => w.Length);
        if (word is not null)
        {
            var pattern = "%" + word + "%";
            query = query.Where(t => EF.Functions.Like(t.RawLabel ?? t.Label, pattern));
        }
        var candidates = await query.Include(t => t.Lines).ThenInclude(l => l.Tags)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .ToListAsync(ct);

        return
        [
            .. candidates
                .Where(t => LabelKey.From(t.RawLabel ?? t.Label) == key
                    && ((payeeId is not null && t.PayeeId != payeeId) || (categoryId is not null && t.Lines[0].CategoryId != categoryId)))
                .Take(MaxSimilar),
        ];
    }

    private static async Task<HashSet<Guid>> ActiveCategoriesAsync(AppDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return wanted.Count == 0
            ? []
            : [.. await db.Categories.AsNoTracking().Where(c => wanted.Contains(c.Id) && !c.IsArchived).Select(c => c.Id).ToListAsync(ct)];
    }
}
