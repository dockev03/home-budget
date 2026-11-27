using System.Security.Claims;

using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Features.Auth;

/// <param name="TwoFactorRequired">Double authentification imposée : l'application reste fermée tant qu'elle n'est pas activée.</param>
/// <param name="AvatarUpdatedAt">Date de la photo de profil (version de <c>/api/account/avatar</c>), ou null sans photo.</param>
public sealed record CurrentUser(
    Guid Id, string UserName, string? Email, IReadOnlyList<string> Roles, bool TwoFactorEnabled, string DisplayName, bool TwoFactorRequired,
    DateTimeOffset? AvatarUpdatedAt)
{
    public static async Task<CurrentUser> FromAsync(UserManager<AppUser> users, AppUser user) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email, [.. await users.GetRolesAsync(user)], user.TwoFactorEnabled,
            user.Name, user.TwoFactorRequired, user.AvatarUpdatedAt);
}

internal static class UserManagerExtensions
{
    /// <summary>Utilisateur de la session courante, ou null si la session ne correspond plus à un compte.</summary>
    public static Task<AppUser?> GetCurrentAsync(this UserManager<AppUser> users, ClaimsPrincipal principal) =>
        users.GetUserAsync(principal);
}
