using HomeBudget.Api.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Webhooks;

/// <summary>Passe la file d'attente des webhooks (I5) toutes les <c>HB__Webhooks__IntervalSeconds</c> secondes.</summary>
public sealed partial class WebhookWorker(
    IServiceScopeFactory scopes, IOptions<HomeBudgetOptions> options, TimeProvider clock, ILogger<WebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Webhooks;
        if (!settings.Enabled)
        {
            LogDisabled();
            return;
        }
        var interval = TimeSpan.FromSeconds(Math.Max(1, settings.IntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, clock, stoppingToken);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>().DispatchDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Envoi des webhooks désactivé (HB__Webhooks__Enabled=false).")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec du traitement de la file des webhooks.")]
    private partial void LogFailed(Exception exception);
}
