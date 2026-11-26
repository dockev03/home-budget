using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Validation de la session à chaque requête : un utilisateur désactivé, ou dont l'empreinte de sécurité a
/// changé (mot de passe, rôle, double authentification), perd aussitôt ses autres sessions. Remplace la
/// validation périodique d'Identity (toutes les 30 minutes par défaut).
/// </summary>
public static class SessionValidator
{
    /// <summary>Clé de <see cref="HttpContext.Items"/> où l'utilisateur validé est conservé pour la requête.</summary>
    public const string UserItemKey = "hb.user";

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var principal = context.Principal;
        var user = principal is null ? null : await users.GetUserAsync(principal);
        var stamp = principal?.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value;
        if (user is null || !user.IsActive || !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        context.HttpContext.Items[UserItemKey] = user;
    }

    /// <summary>Utilisateur de la session courante, validé par <see cref="ValidateAsync"/>.</summary>
    public static AppUser? SessionUser(this HttpContext context) => context.Items[UserItemKey] as AppUser;
}
