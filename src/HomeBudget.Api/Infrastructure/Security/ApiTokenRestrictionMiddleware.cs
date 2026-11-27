using HomeBudget.Api.Domain.Integrations;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Limites d'un jeton d'accès personnel (I5) : il ne touche ni à l'authentification, ni au compte de l'utilisateur,
/// ni à l'administration (utilisateurs, sauvegardes, journal, jetons, webhooks), et un jeton en lecture n'envoie
/// que des requêtes GET.
/// </summary>
public sealed class ApiTokenRestrictionMiddleware(RequestDelegate next)
{
    private static readonly string[] DeniedPrefixes =
    [
        "/api/auth", "/api/account", "/api/setup", "/api/users", "/api/invitations", "/api/backups",
        "/api/audit", "/api/tokens", "/api/webhooks", "/api/notifications", "/api/docs",
    ];

    public Task InvokeAsync(HttpContext context)
    {
        if (ApiTokens.ScopeOf(context.User) is { } scope)
        {
            var path = context.Request.Path;
            if (Array.Exists(DeniedPrefixes, p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                return Forbidden(context, "Cette partie de l'API n'est accessible qu'avec une session ouverte dans l'application.");
            }
            if (scope == ApiTokenScope.Read && !IsSafe(context.Request.Method))
            {
                return Forbidden(context, "Ce jeton est en lecture seule.");
            }
        }
        return next(context);
    }

    private static bool IsSafe(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static Task Forbidden(HttpContext context, string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Accès refusé au jeton.", detail: detail).ExecuteAsync(context);
}
