using HomeBudget.Api.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Backups;

/// <summary>
/// Déclenche la sauvegarde chaque jour à l'heure configurée. Au démarrage, rattrape la sauvegarde
/// du jour si elle manque (conteneur arrêté pendant la nuit, par exemple).
/// </summary>
public sealed partial class BackupWorker(
    BackupService backups,
    IOptions<HomeBudgetOptions> options,
    TimeProvider clock,
    ILogger<BackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Backup;
        if (!settings.Enabled)
        {
            LogDisabled();
            return;
        }

        if (!backups.List().Contains(backups.Today))
        {
            await RunSafelyAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun(clock.GetLocalNow(), settings.Hour);
            await Task.Delay(delay, clock, stoppingToken);
            await RunSafelyAsync(stoppingToken);
        }
    }

    /// <summary>Durée jusqu'à la prochaine occurrence de <paramref name="hour"/> heure locale.</summary>
    public static TimeSpan DelayUntilNextRun(DateTimeOffset now, int hour)
    {
        var next = new DateTimeOffset(now.Date.AddHours(Math.Clamp(hour, 0, 23)), now.Offset);
        if (next <= now)
        {
            next = next.AddDays(1);
        }
        return next - now;
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            await backups.RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sauvegardes automatiques désactivées (HB__Backup__Enabled=false).")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de la sauvegarde automatique.")]
    private partial void LogFailed(Exception exception);
}
