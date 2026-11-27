namespace HomeBudget.Api.Domain.Users;

/// <summary>
/// Photo de profil d'un utilisateur, affichée dans la pastille du compte. Recadrée et réduite par le navigateur avant
/// l'envoi, elle pèse quelques dizaines de Ko : elle est rangée dans la base, à part de <see cref="AppUser"/> pour ne pas
/// être chargée à chaque requête. Comme les comptes utilisateurs, elle ne fait pas partie de la sauvegarde du foyer.
/// </summary>
public sealed class UserAvatar
{
    /// <summary>Taille maximale reçue (256 Kio).</summary>
    public const int MaxSize = 256 * 1024;

    public const int MaxContentTypeLength = 32;

    public Guid UserId { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];
}
