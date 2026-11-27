using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Domain.Users;

public sealed class AppUser : IdentityUser<Guid>
{
    public const int MaxDisplayNameLength = 60;

    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public AppUser(string userName) : this()
    {
        UserName = userName;
    }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Nom affiché comme auteur des opérations (K2) ; l'identifiant sinon.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Un utilisateur désactivé ne peut plus se connecter et ses sessions sont fermées (K1).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Double authentification imposée (utilisateurs invités) : l'API reste fermée tant qu'elle n'est pas active.</summary>
    public bool TwoFactorRequired { get; set; }

    /// <summary>Date du dernier changement de la photo de profil, ou null sans photo ; sert aussi de version pour le cache.</summary>
    public DateTimeOffset? AvatarUpdatedAt { get; set; }

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? UserName ?? string.Empty : DisplayName;
}
