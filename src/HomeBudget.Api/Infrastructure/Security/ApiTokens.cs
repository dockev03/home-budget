using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using HomeBudget.Api.Domain.Integrations;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>Jetons d'accès personnels (I5) : génération, empreinte et lecture des revendications d'une requête.</summary>
public static class ApiTokens
{
    public const string Prefix = "hb_";

    /// <summary>Schéma d'authentification des requêtes portant <c>Authorization: Bearer hb_…</c>.</summary>
    public const string Scheme = "ApiToken";

    /// <summary>Schéma par défaut : jeton si l'en-tête Authorization est présent, cookie de session sinon.</summary>
    public const string SelectorScheme = "HomeBudget";

    public const string TokenIdClaim = "hb:token";
    public const string ScopeClaim = "hb:scope";

    /// <summary>Génère un jeton : 32 octets aléatoires en base64url, préfixés par « hb_ ».</summary>
    public static (string Token, string Hash, string DisplayPrefix) Generate()
    {
        var token = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token), token[..ApiToken.PrefixLength]);
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Vrai si la requête porte un jeton (en-tête Authorization: Bearer).</summary>
    public static bool HasBearer(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    /// <summary>Droit du jeton qui authentifie la requête, ou null pour une session par cookie.</summary>
    public static ApiTokenScope? ScopeOf(ClaimsPrincipal user) =>
        Enum.TryParse<ApiTokenScope>(user.FindFirstValue(ScopeClaim), out var scope) && user.HasClaim(c => c.Type == TokenIdClaim)
            ? scope
            : null;

    public static bool IsTokenRequest(this HttpContext context) => ScopeOf(context.User) is not null;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
