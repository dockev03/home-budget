using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HomeBudget.Api.Infrastructure;

/// <summary>
/// Description OpenAPI de l'API publique (I5) : titre, mode d'emploi et schéma d'authentification par jeton
/// d'accès personnel (<c>Authorization: Bearer hb_…</c>), appliqué à toutes les opérations.
/// </summary>
public sealed class OpenApiSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Home Budget API";
        document.Info.Description =
            "API REST de Home Budget.\n\n" +
            "**Depuis un script** : créez un jeton d'accès personnel (Profil → Accès API) et envoyez-le dans l'en-tête " +
            "`Authorization: Bearer hb_…`. Un jeton en lecture n'envoie que des GET ; un jeton en écriture agit comme un " +
            "membre du foyer, jamais comme administrateur. Débit limité par jeton (120 requêtes par minute par défaut).\n\n" +
            "**Depuis le navigateur** : la session (cookie) suffit, et les requêtes POST/PUT/PATCH/DELETE exigent en plus " +
            "l'en-tête X-CSRF-TOKEN.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiTokens.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "hb_…",
            Description = "Jeton d'accès personnel.",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiTokens.Scheme, document)] = [],
        });
        return Task.CompletedTask;
    }
}
