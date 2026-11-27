namespace HomeBudget.Api.Domain.Audit;

/// <summary>Événements tracés au journal d'audit (N6).</summary>
public enum AuditAction
{
    LoginSucceeded,
    LoginFailed,
    LoginLockedOut,
    PasswordChanged,
    TwoFactorEnabled,
    TwoFactorDisabled,
    UserInvited,
    InvitationRevoked,
    InvitationAccepted,
    RoleChanged,
    UserDeactivated,
    UserReactivated,
    AccountVisibilityChanged,
    Exported,
    BackupExported,
    Restored,
    Deleted,
    ApiTokenCreated,
    ApiTokenRevoked,
    WebhookCreated,
    WebhookUpdated,
    WebhookDisabled,
    AttachmentsRestored,
    SessionRevoked,
}

/// <summary>
/// Entrée du journal d'audit : jamais modifiée par l'application, ni remplacée par une restauration. Seules les
/// connexions (<see cref="LoginActions"/>) peuvent être purgées, si une durée de conservation est configurée.
/// Le nom de l'utilisateur est copié, pour rester lisible s'il est renommé.
/// </summary>
public sealed class AuditLog : Entity
{
    public const int MaxUserNameLength = 64;
    public const int MaxTargetLength = 100;
    public const int MaxDetailsLength = 500;
    public const int MaxUserAgentLength = 512;

    /// <summary>Tentatives de connexion : l'historique des connexions de chaque utilisateur.</summary>
    public static readonly AuditAction[] LoginActions = [AuditAction.LoginSucceeded, AuditAction.LoginFailed, AuditAction.LoginLockedOut];

    public DateTimeOffset At { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Type d'élément concerné (« Transaction », « User »…), ou utilisateur visé.</summary>
    public string? Target { get; set; }

    public Guid? TargetId { get; set; }

    public string? Details { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>En-tête User-Agent de la requête (navigateur, système), déclaré par le client.</summary>
    public string? UserAgent { get; set; }

    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
