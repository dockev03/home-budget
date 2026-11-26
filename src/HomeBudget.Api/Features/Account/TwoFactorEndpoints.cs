using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Features.Account;

/// <summary>Gestion de la double authentification (TOTP) de l'utilisateur connecté.</summary>
public static class TwoFactorEndpoints
{
    private const string Issuer = "Home Budget";
    private const int RecoveryCodeCount = 10;

    public static IEndpointRouteBuilder MapTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account/two-factor").WithTags("Account");

        group.MapGet("/", GetStatusAsync)
            .WithSummary("État de la double authentification.");
        group.MapPost("/setup", SetupAsync)
            .WithSummary("Génère une nouvelle clé TOTP (à scanner dans l'application d'authentification).");
        group.MapPost("/enable", EnableAsync)
            .WithSummary("Active la double authentification après vérification d'un code ; renvoie les codes de secours.");
        group.MapPost("/disable", DisableAsync).RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Désactive la double authentification (mot de passe requis).");
        group.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync).RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Régénère les codes de secours (mot de passe requis).");

        return app;
    }

    public sealed record TwoFactorStatus(bool Enabled, int RecoveryCodesLeft);

    public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

    public sealed record EnableTwoFactorRequest([property: Required] string Code);

    public sealed record PasswordConfirmation([property: Required] string Password);

    public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

    private static async Task<Results<Ok<TwoFactorStatus>, UnauthorizedHttpResult>> GetStatusAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        return TypedResults.Ok(new TwoFactorStatus(user.TwoFactorEnabled, await users.CountRecoveryCodesAsync(user)));
    }

    private static async Task<Results<Ok<AuthenticatorSetup>, ProblemHttpResult, UnauthorizedHttpResult>> SetupAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        if (user.TwoFactorEnabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "La double authentification est déjà active.");
        }

        await users.ResetAuthenticatorKeyAsync(user);
        // La nouvelle clé change l'empreinte de sécurité : la session courante est renouvelée, les autres fermées.
        await signIn.RefreshSignInAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("Clé d'authentification non générée.");
        var label = Uri.EscapeDataString($"{Issuer}:{user.UserName}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period=30";
        return TypedResults.Ok(new AuthenticatorSetup(FormatKey(key), uri));
    }

    private static async Task<Results<Ok<RecoveryCodes>, ProblemHttpResult, UnauthorizedHttpResult>> EnableAsync(
        EnableTwoFactorRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, AuditTrail audit)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        if (user.TwoFactorEnabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "La double authentification est déjà active.");
        }
        if (await users.GetAuthenticatorKeyAsync(user) is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Générez d'abord une clé (étape « setup »).");
        }

        var valid = await users.VerifyTwoFactorTokenAsync(
            user, users.Options.Tokens.AuthenticatorTokenProvider, AuthEndpoints.NormalizeCode(request.Code));
        if (!valid)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Code invalide. Vérifiez l'heure de votre téléphone.");
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
        await signIn.RefreshSignInAsync(user);
        await audit.RecordAsync(AuditAction.TwoFactorEnabled, actor: user);
        return TypedResults.Ok(new RecoveryCodes([.. codes ?? []]));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> DisableAsync(
        PasswordConfirmation request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, AuditTrail audit)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        if (user.TwoFactorRequired)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "La double authentification est imposée à votre compte.",
                detail: "Régénérez vos codes de secours si vous changez de téléphone.");
        }
        var check = await CheckPasswordAsync(signIn, user, request.Password);
        if (check is not null)
        {
            return check;
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        await audit.RecordAsync(AuditAction.TwoFactorDisabled, actor: user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RecoveryCodes>, ProblemHttpResult, UnauthorizedHttpResult>> RegenerateRecoveryCodesAsync(
        PasswordConfirmation request, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        if (!user.TwoFactorEnabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "La double authentification n'est pas active.");
        }
        var check = await CheckPasswordAsync(signIn, user, request.Password);
        if (check is not null)
        {
            return check;
        }

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
        return TypedResults.Ok(new RecoveryCodes([.. codes ?? []]));
    }

    /// <summary>Vérifie le mot de passe en comptant les échecs (verrouillage).</summary>
    private static async Task<ProblemHttpResult?> CheckPasswordAsync(SignInManager<AppUser> signIn, AppUser user, string password)
    {
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return null;
        }
        return result.IsLockedOut
            ? TypedResults.Problem(statusCode: StatusCodes.Status423Locked, title: "Compte temporairement verrouillé.")
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Mot de passe incorrect.");
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
