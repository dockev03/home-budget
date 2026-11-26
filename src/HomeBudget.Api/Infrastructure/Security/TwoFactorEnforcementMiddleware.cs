namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Double authentification imposée (utilisateurs invités) : tant qu'elle n'est pas activée, seules la
/// session et la configuration de la double authentification restent accessibles.
/// </summary>
public sealed class TwoFactorEnforcementMiddleware(RequestDelegate next)
{
    public const string TypeUri = "urn:home-budget:two-factor-required";

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (context.SessionUser() is { TwoFactorRequired: true, TwoFactorEnabled: false }
            && path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/api/auth")
            && !path.StartsWithSegments("/api/account/two-factor"))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Double authentification requise.",
                detail: "Activez la double authentification pour accéder à l'application.",
                type: TypeUri).ExecuteAsync(context);
        }
        return next(context);
    }
}
