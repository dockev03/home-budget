using HomeBudget.Api.Domain.Notifications;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Notifications;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Account;

/// <summary>
/// Envoie les alertes de sécurité mises en file (N9), si l'utilisateur les a demandées et a une adresse e-mail.
/// Un échec d'envoi est journalisé sans nouvelle tentative : l'alerte n'a de sens qu'au moment de l'événement.
/// </summary>
public sealed partial class SecurityAlertWorker(
    SecurityAlertQueue queue,
    IServiceScopeFactory scopes,
    IEmailSender email,
    IOptions<HomeBudgetOptions> options,
    ILogger<SecurityAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var alert in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(alert, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex, alert.Kind, alert.UserId);
            }
        }
    }

    private async Task SendAsync(SecurityAlert alert, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(alert.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }
        var preference = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct)
            ?? new NotificationPreference { UserId = user.Id };
        var wanted = alert.Kind == SecurityAlertKind.Login ? preference.LoginEmailAlert : preference.LockoutEmailAlert;
        if (!wanted)
        {
            return;
        }
        await email.SendAsync(SecurityAlertEmail.Build(user, alert, options.Value.PublicUrl, TimeZoneInfo.Local), ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de l'envoi de l'alerte de sécurité {Kind} à {UserId}.")]
    private partial void LogFailed(Exception exception, SecurityAlertKind kind, Guid userId);
}
