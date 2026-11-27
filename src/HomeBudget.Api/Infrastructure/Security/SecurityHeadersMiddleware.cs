namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// En-têtes de sécurité ajoutés à toutes les réponses. HSTS est posé par le reverse proxy (NPM).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // 'unsafe-inline' pour les styles uniquement : Vuetify injecte la feuille de style du thème.
    // Les scripts restent strictement limités à 'self'.
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; manifest-src 'self'; worker-src 'self'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    // Documentation de l'API (Scalar, réservée aux administrateurs) : nécessite des scripts inline.
    public const string DocsContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    // Pièces jointes (B7) : servies sans rien pouvoir exécuter, ni script, ni formulaire, ni sous-ressource.
    public const string AttachmentContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;
            headers.ContentSecurityPolicy = path.StartsWithSegments("/api/docs") ? DocsContentSecurityPolicy
                : path.StartsWithSegments("/api/attachments") ? AttachmentContentSecurityPolicy
                : ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
            }
            return Task.CompletedTask;
        });
        return next(context);
    }
}
