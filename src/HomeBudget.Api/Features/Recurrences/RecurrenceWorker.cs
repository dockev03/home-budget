using HomeBudget.Api.Infrastructure.Backups;
using HomeBudget.Api.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Recurrences;

/// <summary>
/// Traite les échéances au démarrage (rattrapage après un arrêt), puis chaque jour à l'heure configurée
/// (par défaut juste après minuit, pour que l'opération du jour existe dès le matin).
/// </summary>
public sealed partial class RecurrenceWorker(
    IServiceScopeFactory scopes,
    IOptions<HomeBudgetOptions> options,
    TimeProvider clock,
    ILogger<RecurrenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Recurrences;
        if (!settings.Enabled)
        {
            LogDisabled();
            return;
        }

        await RunSafelyAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(BackupWorker.DelayUntilNextRun(clock.GetLocalNow(), settings.Hour), clock, stoppingToken);
            await RunSafelyAsync(stoppingToken);
        }
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<RecurrenceGenerator>().RunAsync(ct);
            LogProcessed(count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Récurrences désactivées (HB__Recurrences__Enabled=false).")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} échéance(s) de récurrence traitée(s).")]
    private partial void LogProcessed(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du traitement des récurrences.")]
    private partial void LogFailed(Exception exception);
}
