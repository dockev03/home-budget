namespace HomeBudget.Api.Domain.Integrations;

/// <summary>Droit d'un jeton d'accès personnel (I5).</summary>
public enum ApiTokenScope
{
    /// <summary>Lecture seule : requêtes GET.</summary>
    Read,

    /// <summary>Lecture et écriture, dans la limite du rôle de l'utilisateur.</summary>
    Write,
}

/// <summary>
/// Jeton d'accès personnel à l'API (I5), envoyé dans l'en-tête <c>Authorization: Bearer hb_…</c>. Seule son
/// empreinte SHA-256 est stockée : le jeton n'est montré qu'à sa création. Il agit au nom de son utilisateur,
/// jamais avec les droits d'administration. Révoquer un jeton le supprime.
/// </summary>
public sealed class ApiToken : Entity
{
    public const int MaxNameLength = 60;
    public const int PrefixLength = 10;

    /// <summary>Nombre maximal de jetons par utilisateur.</summary>
    public const int MaxPerUser = 20;

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>Empreinte SHA-256 du jeton, en hexadécimal minuscule.</summary>
    public required string Hash { get; set; }

    /// <summary>Début du jeton (« hb_xxxxxxx »), pour le reconnaître dans la liste.</summary>
    public required string Prefix { get; set; }

    public ApiTokenScope Scope { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
