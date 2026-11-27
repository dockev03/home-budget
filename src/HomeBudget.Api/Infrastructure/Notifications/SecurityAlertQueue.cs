using System.Threading.Channels;

using HomeBudget.Api.Domain.Users;

namespace HomeBudget.Api.Infrastructure.Notifications;

public enum SecurityAlertKind
{
    /// <summary>Nouvelle connexion au compte.</summary>
    Login,

    /// <summary>Compte verrouillé après trop d'échecs.</summary>
    LockedOut,
}

/// <summary>Événement de sécurité à signaler par e-mail (N9), selon les préférences de l'utilisateur.</summary>
/// <param name="Origin">Méthode de connexion (connexion seulement).</param>
/// <param name="LockedUntil">Fin du verrouillage (verrouillage seulement).</param>
public sealed record SecurityAlert(
    SecurityAlertKind Kind,
    Guid UserId,
    DateTimeOffset At,
    string? IpAddress,
    string? UserAgent,
    SessionOrigin? Origin = null,
    DateTimeOffset? LockedUntil = null);

/// <summary>
/// File des alertes de sécurité, vidée par <c>SecurityAlertWorker</c> : la connexion n'attend jamais le serveur SMTP
/// et n'échoue pas s'il est en panne. File bornée : au-delà, les alertes sont abandonnées (et journalisées).
/// </summary>
public sealed partial class SecurityAlertQueue(IEmailSender email, ILogger<SecurityAlertQueue> logger)
{
    private const int Capacity = 100;

    private readonly Channel<SecurityAlert> _channel = Channel.CreateBounded<SecurityAlert>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<SecurityAlert> Reader => _channel.Reader;

    /// <summary>Met l'alerte en file si l'envoi d'e-mails est configuré ; les préférences sont lues à l'envoi.</summary>
    public void Enqueue(SecurityAlert alert)
    {
        if (!email.IsConfigured)
        {
            return;
        }
        if (!_channel.Writer.TryWrite(alert))
        {
            LogDropped(alert.Kind, alert.UserId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "File des alertes de sécurité pleine : alerte {Kind} pour {UserId} abandonnée.")]
    private partial void LogDropped(SecurityAlertKind kind, Guid userId);
}
