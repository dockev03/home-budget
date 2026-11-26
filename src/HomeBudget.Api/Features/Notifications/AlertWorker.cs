using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Notifications;

using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Notifications;

/// <summary>
/// Vérifie les alertes (M1) et envoie les résumés (M2) toutes les heures. Inactif si ni le push ni l'e-mail ne
/// sont configurés.
/// </summary>
public sealed partial class AlertWorker(
    IServiceScopeFactory scopes,
    IOptions<HomeBudgetOptions> options,
    WebPushSender push,
    IEmailSender email,
    TimeProvider clock,
    ILogger<AlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Notifications;
        if (!settings.Enabled || (!push.IsConfigured && !email.IsConfigured))
        {
            LogDisabled();
            return;
        }

        // Laisse le démarrage (migrations, récurrences du jour) se terminer avant la première vérification.
        await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, settings.IntervalMinutes)), clock);
        do
        {
            await RunSafelyAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<AlertService>().RunAsync(ct);
            if (result.PushSent + result.EmailsSent > 0)
            {
                LogSent(result.PushSent, result.EmailsSent);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notifications inactives : ni clés VAPID ni serveur SMTP configurés, ou HB__Notifications__Enabled=false.")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Push} alerte(s) push et {Emails} résumé(s) par e-mail envoyés.")]
    private partial void LogSent(int push, int emails);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de la vérification des alertes.")]
    private partial void LogFailed(Exception exception);
}
