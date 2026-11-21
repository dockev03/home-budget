using System.Net;

using HomeBudget.Api.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Restreint /metrics aux réseaux autorisés (par défaut la boucle locale) et refuse toute requête
/// arrivée par le reverse proxy : les métriques ne doivent jamais être exposées sur Internet.
/// Répond 404 pour ne pas révéler l'existence de l'endpoint.
/// </summary>
public sealed class MetricsAccessMiddleware(RequestDelegate next, IOptions<HomeBudgetOptions> options)
{
    private readonly IpNetworkList _allowed = IpNetworkList.Parse(options.Value.Metrics.AllowedNetwork);

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/metrics") && !IsAllowed(context))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(context);
    }

    private bool IsAllowed(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (headers.ContainsKey("X-Forwarded-For") || headers.ContainsKey("X-Original-For"))
        {
            return false;
        }
        var remote = context.Connection.RemoteIpAddress;
        return remote is not null && (IPAddress.IsLoopback(remote) || _allowed.Contains(remote));
    }
}
