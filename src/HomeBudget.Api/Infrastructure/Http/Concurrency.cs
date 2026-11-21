using HomeBudget.Api.Domain;

using Microsoft.Net.Http.Headers;

namespace HomeBudget.Api.Infrastructure.Http;

/// <summary>
/// Concurrence optimiste : un PUT ou un DELETE exige l'en-tête <c>If-Match: &lt;version&gt;</c>
/// (avec ou sans guillemets). Version absente : 428 ; version périmée : 412.
/// </summary>
public static class Concurrency
{
    public static IResult? Check(HttpRequest request, AuditableEntity entity)
    {
        var header = request.Headers[HeaderNames.IfMatch].ToString().Trim().Trim('"');
        if (string.IsNullOrEmpty(header) || !int.TryParse(header, System.Globalization.CultureInfo.InvariantCulture, out var expected))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "En-tête If-Match manquant.",
                detail: "Indiquez la version de l'élément modifié.");
        }
        return expected == entity.Version ? null : Conflict();
    }

    public static IResult Conflict() => TypedResults.Problem(
        statusCode: StatusCodes.Status412PreconditionFailed,
        title: "Modifié entre-temps.",
        detail: "Cet élément a été modifié par ailleurs : rechargez-le puis recommencez.");
}

public static class ConcurrencyEndpointExtensions
{
    /// <summary>Documente les réponses d'un endpoint soumis à If-Match (OpenAPI).</summary>
    public static RouteHandlerBuilder WithConcurrencyProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
