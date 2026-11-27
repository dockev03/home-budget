using HomeBudget.Api.Infrastructure.Data;

namespace HomeBudget.Api.Features.Attachments;

/// <summary>
/// Efface chaque jour les fichiers de pièces jointes qu'aucune opération n'utilise plus : une opération supprimée
/// emporte ses pièces jointes en base, et le fichier reste jusqu'à ce passage.
/// </summary>
public sealed partial class AttachmentCleanupWorker(
    IServiceScopeFactory scopes, AttachmentStore store, TimeProvider clock, ILogger<AttachmentCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse l'application démarrer (migrations) avant le premier passage.
        await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var purged = await store.PurgeOrphansAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), stoppingToken);
                if (purged > 0)
                {
                    LogPurged(purged);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
            await Task.Delay(TimeSpan.FromDays(1), clock, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} fichier(s) de pièce jointe sans opération effacé(s).")]
    private partial void LogPurged(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du nettoyage des pièces jointes.")]
    private partial void LogFailed(Exception exception);
}
