using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/csrf", GetCsrfToken).AllowAnonymous()
            .WithSummary("Jeton anti-CSRF à renvoyer dans l'en-tête X-CSRF-TOKEN.");
        group.MapGet("/me", GetMeAsync)
            .WithSummary("Utilisateur connecté.");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Connexion par identifiant et mot de passe.");
        group.MapPost("/login/two-factor", LoginTwoFactorAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Seconde étape de connexion : code TOTP.");
        group.MapPost("/login/recovery-code", LoginRecoveryCodeAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Seconde étape de connexion : code de secours.");
        group.MapPost("/logout", LogoutAsync)
            .WithSummary("Déconnexion.");

        return app;
    }

    public sealed record CsrfToken(string Token);

    public sealed record LoginRequest(
        [property: Required] string UserName,
        [property: Required] string Password,
        bool RememberMe = false);

    public sealed record TwoFactorLoginRequest([property: Required] string Code, bool RememberMe = false);

    public sealed record RecoveryCodeLoginRequest([property: Required] string RecoveryCode);

    public enum LoginStatus
    {
        Succeeded,
        RequiresTwoFactor,
    }

    public sealed record LoginResponse(LoginStatus Status, CurrentUser? User);

    private static Ok<CsrfToken> GetCsrfToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return TypedResults.Ok(new CsrfToken(tokens.RequestToken!));
    }

    private static async Task<Results<Ok<CurrentUser>, UnauthorizedHttpResult>> GetMeAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetCurrentAsync(principal);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(await CurrentUser.FromAsync(users, user));
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditTrail audit)
    {
        var userName = request.UserName.Trim();
        var user = await users.FindByNameAsync(userName);
        if (user is { IsActive: false })
        {
            // Même réponse qu'un mot de passe faux, sans le vérifier ni compter d'échec.
            await audit.RecordAsync(AuditAction.LoginFailed, details: "Compte désactivé.", actor: user);
            return Incorrect();
        }
        var result = await signIn.PasswordSignInAsync(userName, request.Password, request.RememberMe, lockoutOnFailure: true);
        return await ToResponseAsync(result, signIn, users, audit, user, userName, "mot de passe");
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginTwoFactorAsync(
        TwoFactorLoginRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditTrail audit)
    {
        var code = NormalizeCode(request.Code);
        // Récupéré avant la connexion : après succès, le cookie de seconde étape est supprimé.
        var pending = await signIn.GetTwoFactorAuthenticationUserAsync();
        var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, request.RememberMe, rememberClient: false);
        return await ToResponseAsync(result, signIn, users, audit, pending, null, "code TOTP");
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginRecoveryCodeAsync(
        RecoveryCodeLoginRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditTrail audit)
    {
        // Format « XXXXX-XXXXX » : le tiret fait partie du code.
        var code = request.RecoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        var pending = await signIn.GetTwoFactorAuthenticationUserAsync();
        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(code);
        return await ToResponseAsync(result, signIn, users, audit, pending, null, "code de secours");
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> ToResponseAsync(
        SignInResult result, SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditTrail audit,
        AppUser? user, string? attemptedUserName, string method)
    {
        if (result.Succeeded)
        {
            if (user is null || !user.IsActive)
            {
                // Désactivé entre les deux étapes de connexion.
                await signIn.SignOutAsync();
                return Incorrect();
            }
            await audit.RecordAsync(AuditAction.LoginSucceeded, details: $"Connexion par {method}.", actor: user);
            return TypedResults.Ok(new LoginResponse(LoginStatus.Succeeded, await CurrentUser.FromAsync(users, user)));
        }
        if (result.RequiresTwoFactor)
        {
            return TypedResults.Ok(new LoginResponse(LoginStatus.RequiresTwoFactor, null));
        }
        if (result.IsLockedOut)
        {
            await audit.RecordAsync(AuditAction.LoginLockedOut, actor: user, actorName: attemptedUserName);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Compte temporairement verrouillé.",
                detail: "Trop de tentatives infructueuses. Réessayez dans 15 minutes.");
        }
        await audit.RecordAsync(
            AuditAction.LoginFailed,
            details: user is null ? "Identifiant inconnu." : $"Échec : {method}.",
            actor: user,
            actorName: attemptedUserName);
        return Incorrect();
    }

    private static ProblemHttpResult Incorrect() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Identifiants ou code incorrects.");

    internal static string NormalizeCode(string code) =>
        code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
}
