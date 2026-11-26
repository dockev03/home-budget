using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Features.Account;

/// <summary>Changement du mot de passe de l'utilisateur connecté.</summary>
public static class PasswordEndpoints
{
    public static IEndpointRouteBuilder MapPasswordEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/account/password", ChangePasswordAsync).WithTags("Account").RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Change le mot de passe (mot de passe actuel requis) ; les autres sessions sont fermées.");
        return app;
    }

    public sealed record ChangePasswordRequest(
        [property: Required] string CurrentPassword,
        [property: Required, StringLength(256)] string NewPassword);

    private static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, AuditTrail audit)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        // Vérification comptée dans les échecs : le verrouillage protège aussi ce formulaire.
        var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status423Locked, title: "Compte temporairement verrouillé.");
        }
        if (!check.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["currentPassword"] = ["Mot de passe actuel incorrect."],
            });
        }

        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newPassword"] = [.. changed.Errors.Select(e => e.Description)],
            });
        }
        // Nouvelle empreinte de sécurité : cette session est renouvelée, les autres sont fermées.
        await signIn.RefreshSignInAsync(user);
        await audit.RecordAsync(AuditAction.PasswordChanged, actor: user);
        return TypedResults.NoContent();
    }
}
