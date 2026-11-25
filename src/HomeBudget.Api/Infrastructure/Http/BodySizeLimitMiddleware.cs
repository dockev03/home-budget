using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace HomeBudget.Api.Infrastructure.Http;

/// <summary>
/// Applique la taille maximale déclarée par un endpoint (<see cref="IRequestSizeLimitMetadata"/>) : refus immédiat
/// (413) si l'en-tête Content-Length la dépasse, et limite de lecture du corps pour un envoi sans longueur annoncée.
/// </summary>
public sealed class BodySizeLimitMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is { MaxRequestBodySize: { } max })
        {
            if (context.Request.ContentLength > max)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Requête trop volumineuse.",
                    detail: $"Taille maximale : {max / (1024 * 1024)} Mio.").ExecuteAsync(context);
            }
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = max;
            }
        }
        return next(context);
    }
}
