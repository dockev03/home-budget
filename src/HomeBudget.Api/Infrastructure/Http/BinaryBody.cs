using Microsoft.OpenApi;

namespace HomeBudget.Api.Infrastructure.Http;

public static class BinaryBodyExtensions
{
    /// <summary>
    /// Documente (OpenAPI) un corps de requête envoyé tel quel, lu directement dans le flux par l'endpoint : un fichier
    /// et non du JSON. Sans contrainte de type de contenu au routage, le type réel est vérifié sur les premiers octets.
    /// </summary>
    public static RouteHandlerBuilder WithBinaryBody(this RouteHandlerBuilder builder, params string[] contentTypes) =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            var body = new OpenApiRequestBody { Required = true, Content = new Dictionary<string, OpenApiMediaType>() };
            foreach (var type in contentTypes)
            {
                body.Content[type] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } };
            }
            operation.RequestBody = body;
            return Task.CompletedTask;
        });
}
