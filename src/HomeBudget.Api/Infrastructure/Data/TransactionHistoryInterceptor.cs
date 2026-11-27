using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HomeBudget.Api.Infrastructure.Data;

/// <summary>
/// Historique des modifications des opérations (B13) : à chaque enregistrement, l'état en base de chaque opération
/// modifiée (avant) est comparé à son état suivi (après) ; les champs qui diffèrent sont inscrits dans une
/// <see cref="TransactionChange"/>, enregistrée avec la modification. Toutes les voies de modification passent
/// ici : saisie, modification en masse, pointage, rapprochement, import, liens.
/// </summary>
/// <remarks>La création et la suppression ne sont pas des modifications : elles ont leurs propres traces (K2, N6).</remarks>
public sealed class TransactionHistoryInterceptor(IHttpContextAccessor http, TimeProvider clock) : SaveChangesInterceptor
{
    private const int QueryChunk = 500;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db && Changed(db) is { Count: > 0 } ids)
        {
            var before = new Dictionary<Guid, Transaction>();
            foreach (var chunk in ids.Chunk(QueryChunk))
            {
                foreach (var t in BeforeQuery(db, chunk).ToList())
                {
                    before[t.Id] = t;
                }
            }
            Record(db, ids, before);
        }
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db && Changed(db) is { Count: > 0 } ids)
        {
            var before = new Dictionary<Guid, Transaction>();
            foreach (var chunk in ids.Chunk(QueryChunk))
            {
                foreach (var t in await BeforeQuery(db, chunk).ToListAsync(cancellationToken))
                {
                    before[t.Id] = t;
                }
            }
            Record(db, ids, before);
        }
        return result;
    }

    /// <summary>Opérations existantes dont l'en-tête ou une ligne est modifié.</summary>
    private static List<Guid> Changed(AppDbContext db)
    {
        if (db.PreserveAuditFields)
        {
            return [];
        }
        db.ChangeTracker.DetectChanges();
        var states = db.ChangeTracker.Entries<Transaction>().ToDictionary(e => e.Entity.Id, e => e.State);
        var ids = states.Where(s => s.Value == EntityState.Modified).Select(s => s.Key).ToHashSet();
        foreach (var line in db.ChangeTracker.Entries<TransactionLine>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var id = line.Entity.TransactionId;
            if (!states.TryGetValue(id, out var state) || state is EntityState.Unchanged or EntityState.Modified)
            {
                ids.Add(id);
            }
        }
        return [.. ids];
    }

    private static IQueryable<Transaction> BeforeQuery(AppDbContext db, Guid[] ids) =>
        db.Transactions.AsNoTracking().IgnoreQueryFilters()
            .Include(t => t.Lines).ThenInclude(l => l.Tags)
            .Where(t => ids.Contains(t.Id));

    private void Record(AppDbContext db, List<Guid> ids, Dictionary<Guid, Transaction> before)
    {
        var now = clock.GetUtcNow();
        var user = http.HttpContext?.User;
        Guid? userId = Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : null;
        var userName = AuditLog.Truncate(user?.Identity?.Name, AuditLog.MaxUserNameLength);
        var tracked = db.ChangeTracker.Entries<Transaction>().ToDictionary(e => e.Entity.Id);

        foreach (var id in ids)
        {
            if (!before.TryGetValue(id, out var old))
            {
                continue;
            }
            var oldLines = Snapshot(old.Lines, _ => null);
            List<TransactionLineSnapshot> newLines;
            Transaction current;
            if (tracked.TryGetValue(id, out var entry))
            {
                current = entry.Entity;
                newLines = LinesLoaded(entry) ? Snapshot(current.Lines, line => TagsOf(db, line, old)) : oldLines;
            }
            else
            {
                // Seules des lignes sont suivies : l'en-tête n'a pas changé.
                current = old;
                var lines = db.ChangeTracker.Entries<TransactionLine>()
                    .Where(e => e.Entity.TransactionId == id && e.State != EntityState.Deleted)
                    .Select(e => e.Entity);
                newLines = Snapshot(lines, line => TagsOf(db, line, old));
            }

            var changes = Compare(old, current, oldLines, newLines);
            if (changes.Count > 0)
            {
                db.Add(new TransactionChange
                {
                    TransactionId = id,
                    At = now,
                    UserId = userId,
                    UserName = userName,
                    Changes = JsonSerializer.Serialize(changes, Json),
                });
            }
        }
    }

    private static bool LinesLoaded(EntityEntry<Transaction> entry) =>
        entry.Collection(t => t.Lines).IsLoaded || entry.Entity.Lines.Count > 0;

    /// <summary>Tags d'une ligne suivie : les siens s'ils sont chargés (ou si elle est nouvelle), sinon ceux en base.</summary>
    private static IEnumerable<string>? TagsOf(AppDbContext db, TransactionLine line, Transaction old)
    {
        var entry = db.Entry(line);
        if (entry.State == EntityState.Added || entry.Collection(l => l.Tags).IsLoaded || line.Tags.Count > 0)
        {
            return line.Tags.Select(g => g.Name);
        }
        return old.Lines.FirstOrDefault(l => l.Id == line.Id)?.Tags.Select(g => g.Name);
    }

    private static List<TransactionLineSnapshot> Snapshot(IEnumerable<TransactionLine> lines, Func<TransactionLine, IEnumerable<string>?> tags) =>
        // Montant ramené à 2 décimales exactement : 12,5 et 12,50 ne doivent pas passer pour une modification.
        [.. lines.OrderBy(l => l.SortOrder).Select(l => new TransactionLineSnapshot(
            l.CategoryId, Math.Round(l.Amount * 1.00m, 2), l.Memo, [.. (tags(l) ?? l.Tags.Select(g => g.Name)).Order(StringComparer.Ordinal)]))];

    private static List<TransactionFieldChange> Compare(
        Transaction old, Transaction current, List<TransactionLineSnapshot> oldLines, List<TransactionLineSnapshot> newLines)
    {
        var changes = new List<TransactionFieldChange>();
        void Field(string name, string? before, string? after)
        {
            if (before != after)
            {
                changes.Add(new TransactionFieldChange(name, before, after));
            }
        }
        Field("date", Iso(old.Date), Iso(current.Date));
        Field("account", old.AccountId.ToString(), current.AccountId.ToString());
        Field("payee", old.PayeeId?.ToString(), current.PayeeId?.ToString());
        Field("label", old.Label, current.Label);
        Field("notes", old.Notes, current.Notes);
        Field("cleared", Bool(old.IsCleared), Bool(current.IsCleared));
        Field("reconciled", Bool(old.IsReconciled), Bool(current.IsReconciled));
        Field("reimburses", old.ReimbursesId?.ToString(), current.ReimbursesId?.ToString());
        Field("advance", old.AdvanceId?.ToString(), current.AdvanceId?.ToString());
        Field("referenceMonth", Month(old.ReferenceMonth), Month(current.ReferenceMonth));
        if (JsonSerializer.Serialize(oldLines, Json) != JsonSerializer.Serialize(newLines, Json))
        {
            changes.Add(new TransactionFieldChange("lines", BeforeLines: oldLines, AfterLines: newLines));
        }
        return changes;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Month(DateOnly? month) => month?.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";
}
