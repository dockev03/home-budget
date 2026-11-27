using Microsoft.AspNetCore.Antiforgery;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Exige un jeton anti-CSRF valide (en-tête X-CSRF-TOKEN) sur toutes les requêtes /api
/// qui modifient l'état (POST, PUT, PATCH, DELETE). Le jeton s'obtient via GET /api/auth/csrf.
/// Une requête authentifiée par un jeton d'accès personnel (I5) n'est pas concernée : elle ne porte pas de cookie,
/// et un site tiers ne peut pas ajouter l'en-tête Authorization à la requête d'un navigateur.
/// </summary>
public sealed class AntiforgeryValidationMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    public const string HeaderName = "X-CSRF-TOKEN";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") && IsUnsafe(context.Request.Method) && !context.IsTokenRequest())
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                await Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Jeton anti-CSRF absent ou invalide.",
                    detail: "Rechargez la page puis réessayez.").ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    }

    private static bool IsUnsafe(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));
}
