using System.Text.Json;

using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HomeBudget.Api.Infrastructure.Webhooks;

/// <summary>Opération telle qu'envoyée aux webhooks.</summary>
public sealed record WebhookTransaction(
    Guid Id, Guid AccountId, DateOnly Date, decimal Amount, string? Label, Guid? PayeeId, bool IsCleared, Guid? TransferId);

/// <summary>Import terminé tel qu'envoyé aux webhooks.</summary>
public sealed record WebhookImport(Guid Id, Guid AccountId, string FileName, ImportFormat Format, int Created, int Matched, int Duplicates);

/// <summary>Corps d'un message de webhook.</summary>
/// <param name="Id">Identifiant de l'envoi, repris dans l'en-tête X-HomeBudget-Delivery (idempotence côté destinataire).</param>
public sealed record WebhookMessage(Guid Id, string Event, DateTimeOffset OccurredAt, object Data);

/// <summary>
/// Production des événements des webhooks (I5) : à chaque enregistrement, les opérations créées, modifiées ou
/// supprimées et les imports terminés sont mis en file d'attente (<see cref="WebhookDelivery"/>) pour chaque webhook
/// actif abonné, dans la même transaction que la modification. Comme l'historique (B13), toutes les voies de
/// modification passent ici. Chaque webhook ne reçoit que les comptes que voit son propriétaire (R8).
/// </summary>
public sealed class WebhookEventInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    /// <summary>Nombre maximal d'opérations par message (un import important produit plusieurs messages).</summary>
    public const int MaxItemsPerMessage = 200;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db && Collect(db) is { } events)
        {
            var webhooks = ActiveWebhooks(db).ToList();
            if (webhooks.Count > 0)
            {
                var missing = MissingTransactions(db, events);
                events.Updated.AddRange(missing.Count == 0 ? [] : LoadTransactions(db, missing).ToList());
                Enqueue(db, webhooks, events, LoadAccounts(db, events.AccountIds()).ToList());
            }
        }
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db && Collect(db) is { } events)
        {
            var webhooks = await ActiveWebhooks(db).ToListAsync(cancellationToken);
            if (webhooks.Count > 0)
            {
                var missing = MissingTransactions(db, events);
                if (missing.Count > 0)
                {
                    events.Updated.AddRange(await LoadTransactions(db, missing).ToListAsync(cancellationToken));
                }
                Enqueue(db, webhooks, events, await LoadAccounts(db, events.AccountIds()).ToListAsync(cancellationToken));
            }
        }
        return result;
    }

    private sealed class Events
    {
        public List<Transaction> Created { get; } = [];

        public List<Transaction> Updated { get; } = [];

        public List<Transaction> Deleted { get; } = [];

        public List<ImportBatch> Imports { get; } = [];

        /// <summary>Opérations dont seules des lignes ont changé, sans en-tête suivi.</summary>
        public HashSet<Guid> LinesOnly { get; } = [];

        public HashSet<Guid> AccountIds() =>
            [.. Created.Concat(Updated).Concat(Deleted).Select(t => t.AccountId).Concat(Imports.Select(i => i.AccountId))];
    }

    private static Events? Collect(AppDbContext db)
    {
        // Une restauration recrée les données : ce ne sont pas des événements.
        if (db.PreserveAuditFields)
        {
            return null;
        }
        db.ChangeTracker.DetectChanges();
        var events = new Events();
        var tracked = new Dictionary<Guid, EntityState>();
        foreach (var entry in db.ChangeTracker.Entries<Transaction>())
        {
            tracked[entry.Entity.Id] = entry.State;
            switch (entry.State)
            {
                case EntityState.Added:
                    events.Created.Add(entry.Entity);
                    break;
                case EntityState.Modified:
                    events.Updated.Add(entry.Entity);
                    break;
                case EntityState.Deleted:
                    events.Deleted.Add(entry.Entity);
                    break;
            }
        }
        foreach (var line in db.ChangeTracker.Entries<TransactionLine>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var id = line.Entity.TransactionId;
            if (!tracked.TryGetValue(id, out var state))
            {
                events.LinesOnly.Add(id);
            }
            else if (state == EntityState.Unchanged && events.Updated.TrueForAll(t => t.Id != id))
            {
                events.Updated.Add(db.ChangeTracker.Entries<Transaction>().First(e => e.Entity.Id == id).Entity);
            }
        }
        events.Imports.AddRange(db.ChangeTracker.Entries<ImportBatch>().Where(e => e.State == EntityState.Added).Select(e => e.Entity));
        var any = events.Created.Count + events.Updated.Count + events.Deleted.Count + events.Imports.Count + events.LinesOnly.Count > 0;
        return any ? events : null;
    }

    private static IQueryable<Webhook> ActiveWebhooks(AppDbContext db) => db.Webhooks.AsNoTracking().Where(w => w.IsActive);

    private static List<Guid> MissingTransactions(AppDbContext db, Events events) => [.. events.LinesOnly];

    private static IQueryable<Transaction> LoadTransactions(AppDbContext db, List<Guid> ids) =>
        db.Transactions.AsNoTracking().IgnoreQueryFilters().Where(t => ids.Contains(t.Id));

    private static IQueryable<Account> LoadAccounts(AppDbContext db, HashSet<Guid> ids) =>
        db.Accounts.AsNoTracking().IgnoreQueryFilters().Where(a => ids.Contains(a.Id));

    private void Enqueue(AppDbContext db, List<Webhook> webhooks, Events events, List<Account> stored)
    {
        var accounts = stored.ToDictionary(a => a.Id);
        foreach (var entry in db.ChangeTracker.Entries<Account>().Where(e => e.State != EntityState.Deleted))
        {
            accounts[entry.Entity.Id] = entry.Entity;
        }
        var now = clock.GetUtcNow();
        foreach (var webhook in webhooks)
        {
            // R8 : seuls les comptes partagés et les comptes privés du propriétaire du webhook.
            bool Visible(Guid accountId) => accounts.TryGetValue(accountId, out var a)
                && (a.Visibility == AccountVisibility.Shared || a.OwnerId == webhook.OwnerId);

            void Add(string name, IEnumerable<object> items, Func<object[], object> data)
            {
                if (!webhook.Events.Contains(name))
                {
                    return;
                }
                foreach (var chunk in items.Chunk(MaxItemsPerMessage))
                {
                    var delivery = new WebhookDelivery
                    {
                        WebhookId = webhook.Id,
                        Event = name,
                        Payload = string.Empty,
                        CreatedAt = now,
                        NextAttemptAt = now,
                        Status = WebhookDeliveryStatus.Pending,
                    };
                    delivery.Payload = JsonSerializer.Serialize(new WebhookMessage(delivery.Id, name, now, data(chunk)), Json);
                    db.WebhookDeliveries.Add(delivery);
                }
            }

            object Transactions(object[] items) => new { transactions = items };
            Add(WebhookEvents.TransactionCreated, Snapshot(events.Created.Where(t => Visible(t.AccountId))), Transactions);
            Add(WebhookEvents.TransactionUpdated, Snapshot(events.Updated.Where(t => Visible(t.AccountId))), Transactions);
            Add(WebhookEvents.TransactionDeleted, Snapshot(events.Deleted.Where(t => Visible(t.AccountId))), Transactions);
            foreach (var import in events.Imports.Where(i => Visible(i.AccountId)))
            {
                Add(WebhookEvents.ImportCompleted, [import], items => new WebhookImport(
                    import.Id, import.AccountId, import.FileName, import.Format, import.Created, import.Matched, import.Duplicates));
            }
        }
    }

    private static IEnumerable<object> Snapshot(IEnumerable<Transaction> transactions) =>
        transactions.DistinctBy(t => t.Id).Select(t =>
            (object)new WebhookTransaction(t.Id, t.AccountId, t.Date, t.Amount, t.Label, t.PayeeId, t.IsCleared, t.TransferId));
}
