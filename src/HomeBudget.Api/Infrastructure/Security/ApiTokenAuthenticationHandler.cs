using System.Security.Claims;
using System.Text.Encodings.Web;

using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Authentification par jeton d'accès personnel (I5). Le jeton est retrouvé par son empreinte ; il doit être en cours
/// de validité et son utilisateur actif. Il n'a jamais le rôle administrateur : un jeton en écriture agit comme un
/// membre (si l'utilisateur peut écrire), un jeton en lecture comme un utilisateur en lecture seule.
/// </summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db,
    UserManager<AppUser> users,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Intervalle minimal entre deux mises à jour de la date de dernière utilisation.</summary>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!ApiTokens.HasBearer(Request))
        {
            return AuthenticateResult.NoResult();
        }
        var value = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        if (!value.StartsWith(ApiTokens.Prefix, StringComparison.Ordinal) || value.Length > 100)
        {
            return AuthenticateResult.Fail("Jeton d'accès invalide.");
        }
        var hash = ApiTokens.Hash(value);
        var token = await db.ApiTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Hash == hash, Context.RequestAborted);
        var now = clock.GetUtcNow();
        if (token is null || token.ExpiresAt <= now)
        {
            return AuthenticateResult.Fail("Jeton d'accès invalide ou expiré.");
        }
        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            return AuthenticateResult.Fail("Utilisateur désactivé.");
        }

        var roles = await users.GetRolesAsync(user);
        var canWrite = roles.Contains(Roles.Admin) || roles.Contains(Roles.Member);
        var role = token.Scope == ApiTokenScope.Write && canWrite ? Roles.Member : Roles.ReadOnly;
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                new Claim(ClaimTypes.Role, role),
                new Claim(ApiTokens.TokenIdClaim, token.Id.ToString()),
                new Claim(ApiTokens.ScopeClaim, token.Scope.ToString()),
            ],
            ApiTokens.Scheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        if (token.LastUsedAt is null || now - token.LastUsedAt > LastUsedPrecision)
        {
            await db.ApiTokens.Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), Context.RequestAborted);
        }
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiTokens.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Jeton d'accès invalide ou expiré.").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Accès refusé.",
            detail: "Ce jeton n'a pas les droits nécessaires.").ExecuteAsync(Context);
}
