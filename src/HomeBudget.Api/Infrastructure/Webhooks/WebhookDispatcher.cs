using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Webhooks;

/// <summary>
/// Traitement de la file d'attente des webhooks (I5) : envoie les événements dus, dans l'ordre, et programme une
/// nouvelle tentative après un échec (1 min, 5 min, 30 min, 2 h, 12 h). Après la dernière, l'envoi est abandonné ;
/// au bout de <see cref="Webhook.MaxConsecutiveFailures"/> envois abandonnés à la suite, le webhook est désactivé.
/// </summary>
public sealed partial class WebhookDispatcher(AppDbContext db, WebhookSender sender, TimeProvider clock, ILogger<WebhookDispatcher> logger)
{
    private const int BatchSize = 50;

    /// <summary>Durée de conservation de l'historique des envois terminés.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(30);

    /// <summary>Envoie les événements dus ; renvoie le nombre d'envois tentés.</summary>
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0)
        {
            await PurgeAsync(now, ct);
            return 0;
        }

        var webhookIds = due.Select(d => d.WebhookId).Distinct().ToList();
        var webhooks = await db.Webhooks.AsNoTracking().Where(w => webhookIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        var blocked = new HashSet<Guid>();
        foreach (var delivery in due)
        {
            if (!webhooks.TryGetValue(delivery.WebhookId, out var webhook) || !webhook.IsActive)
            {
                Complete(delivery, WebhookDeliveryStatus.Failed, null, "Webhook désactivé.");
                continue;
            }
            // Ordre des événements : après un échec, les suivants du même webhook attendent leur tour.
            if (blocked.Contains(webhook.Id))
            {
                continue;
            }
            var result = await sender.SendAsync(webhook, delivery, ct);
            delivery.Attempts++;
            if (result.Success)
            {
                Complete(delivery, WebhookDeliveryStatus.Succeeded, result.StatusCode, null);
                if (webhook.ConsecutiveFailures > 0)
                {
                    webhook.ConsecutiveFailures = 0;
                    await SetFailuresAsync(webhook.Id, 0, ct);
                }
                continue;
            }

            blocked.Add(webhook.Id);
            if (delivery.Attempts <= WebhookDelivery.RetryDelays.Count)
            {
                delivery.LastStatusCode = result.StatusCode;
                delivery.LastError = result.Error;
                delivery.NextAttemptAt = clock.GetUtcNow() + WebhookDelivery.RetryDelays[delivery.Attempts - 1];
                continue;
            }
            Complete(delivery, WebhookDeliveryStatus.Failed, result.StatusCode, result.Error);
            webhook.ConsecutiveFailures++;
            await SetFailuresAsync(webhook.Id, webhook.ConsecutiveFailures, ct);
            if (webhook.ConsecutiveFailures >= Webhook.MaxConsecutiveFailures)
            {
                await DisableAsync(webhook, ct);
            }
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private void Complete(WebhookDelivery delivery, WebhookDeliveryStatus status, int? statusCode, string? error)
    {
        delivery.Status = status;
        delivery.CompletedAt = clock.GetUtcNow();
        delivery.LastStatusCode = statusCode;
        delivery.LastError = error;
    }

    // Mises à jour directes : l'état d'envoi ne change ni la version ni l'auteur du webhook (pas de conflit d'édition).
    private Task<int> SetFailuresAsync(Guid id, int failures, CancellationToken ct) =>
        db.Webhooks.Where(w => w.Id == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ConsecutiveFailures, failures), ct);

    private async Task DisableAsync(Webhook webhook, CancellationToken ct)
    {
        webhook.IsActive = false;
        await db.Webhooks.Where(w => w.Id == webhook.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsActive, false), ct);
        // Les envois en attente sont abandonnés avec lui.
        var now = clock.GetUtcNow();
        await db.WebhookDeliveries
            .Where(d => d.WebhookId == webhook.Id && d.Status == WebhookDeliveryStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, WebhookDeliveryStatus.Failed)
                .SetProperty(d => d.CompletedAt, now)
                .SetProperty(d => d.LastError, "Webhook désactivé."), ct);
        db.AuditLogs.Add(new AuditLog
        {
            At = now,
            Action = AuditAction.WebhookDisabled,
            Target = AuditLog.Truncate(webhook.Name, AuditLog.MaxTargetLength),
            TargetId = webhook.Id,
            Details = $"Désactivé après {Webhook.MaxConsecutiveFailures} envois en échec à la suite.",
        });
        LogDisabled(webhook.Name);
    }

    private Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var limit = now - HistoryRetention;
        return db.WebhookDeliveries
            .Where(d => d.Status != WebhookDeliveryStatus.Pending && d.CompletedAt < limit)
            .ExecuteDeleteAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook « {Name} » désactivé après des envois en échec à la suite.")]
    private partial void LogDisabled(string name);
}
