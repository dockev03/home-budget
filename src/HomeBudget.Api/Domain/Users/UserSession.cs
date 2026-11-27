namespace HomeBudget.Api.Domain.Users;

/// <summary>Façon dont la session a été ouverte.</summary>
public enum SessionOrigin
{
    /// <summary>Mot de passe seul (ou création du compte : installation, invitation).</summary>
    Password,

    /// <summary>Mot de passe et code TOTP.</summary>
    Authenticator,

    /// <summary>Mot de passe et code de secours.</summary>
    RecoveryCode,

    /// <summary>Session ouverte avant le suivi des sessions, reprise à sa première requête.</summary>
    Legacy,
}

/// <summary>
/// Session de connexion d'un utilisateur (N8), désignée par la revendication <c>hb_sid</c> du cookie. Une session
/// révoquée (<see cref="RevokedAt"/>) est refusée dès la requête suivante. L'empreinte de sécurité copiée à
/// l'ouverture écarte de la liste les sessions déjà fermées par un changement de mot de passe, de rôle ou de 2FA.
/// </summary>
public sealed class UserSession : Entity
{
    public const int MaxUserAgentLength = 512;
    public const int MaxIpLength = 45;

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Dernière requête, à 5 minutes près.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Adresse IP de la dernière requête (celle du client, derrière le reverse proxy).</summary>
    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public SessionOrigin Origin { get; set; }

    /// <summary>Empreinte de sécurité de l'utilisateur à l'ouverture (ou au dernier rafraîchissement) de la session.</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>Fermeture (révocation, déconnexion) : le cookie est refusé ; la ligne est purgée après expiration.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
