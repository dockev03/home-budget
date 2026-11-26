namespace HomeBudget.Api.Domain.Notifications;

/// <summary>
/// Abonnement Web Push d'un appareil (M1) : l'adresse du service push du navigateur et les clés de chiffrement
/// du message (RFC 8291). Un utilisateur a un abonnement par navigateur ou téléphone.
/// </summary>
public sealed class PushSubscription : Entity
{
    public const int MaxEndpointLength = 1000;
    public const int MaxKeyLength = 200;
    public const int MaxDeviceLength = 100;

    public Guid UserId { get; set; }

    /// <summary>Adresse HTTPS du service push (Google, Mozilla, Apple, Microsoft).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Clé publique P-256 de l'appareil, en base64url.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Secret d'authentification de l'appareil (16 octets), en base64url.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>Navigateur et système, pour reconnaître l'appareil dans la liste.</summary>
    public string? Device { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }
}
