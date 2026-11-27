using System.Globalization;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Account;

/// <summary>
/// Purge chaque jour les connexions du journal d'audit plus anciennes que la durée de conservation
/// (<see cref="SecurityOptions.LoginHistoryRetentionDays"/>). Sans durée configurée, rien n'est effacé.
/// </summary>
public sealed partial class LoginHistoryCleanupWorker(
    IServiceScopeFactory scopes, IOptions<HomeBudgetOptions> options, TimeProvider clock, ILogger<LoginHistoryCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = options.Value.Security.LoginHistoryRetentionDays;
        if (days <= 0)
        {
            return;
        }
        // Laisse l'application démarrer (migrations) avant le premier passage.
        await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var purged = await PurgeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), clock.GetUtcNow(), days, stoppingToken);
                if (purged > 0)
                {
                    LogPurged(purged, days);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
            await Task.Delay(TimeSpan.FromDays(1), clock, stoppingToken);
        }
    }

    /// <summary>Efface les connexions antérieures à <paramref name="now"/> moins <paramref name="days"/> jours.</summary>
    public static Task<int> PurgeAsync(AppDbContext db, DateTimeOffset now, int days, CancellationToken ct)
    {
        // SQLite ne compare pas les DateTimeOffset côté serveur : la date est stockée en texte
        // « aaaa-MM-jj HH:mm:ss.FFFFFFF+00:00 », toujours en UTC, que l'ordre lexicographique suffit à comparer.
        var cutoff = now.ToUniversalTime().AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
        return db.AuditLogs.FromSql($"SELECT * FROM AuditLogs WHERE At < {cutoff}")
            .Where(l => AuditLog.LoginActions.Contains(l.Action))
            .ExecuteDeleteAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} connexion(s) de plus de {Days} jours effacée(s) du journal d'audit.")]
    private partial void LogPurged(int count, int days);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de la purge de l'historique des connexions.")]
    private partial void LogFailed(Exception exception);
}
