using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using HomeBudget.Api.Domain.Notifications;
using HomeBudget.Api.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Notifications;

/// <summary>Contenu d'une notification, lu par le service worker de la PWA.</summary>
/// <param name="Url">Page ouverte au clic, relative à l'application (« /budget »).</param>
/// <param name="Tag">Une notification de même étiquette remplace la précédente sur l'appareil.</param>
public sealed record PushMessage(string Title, string Body, string Url, string Tag);

public enum PushOutcome
{
    Sent,

    /// <summary>Abonnement expiré ou révoqué (404, 410) : il faut le supprimer.</summary>
    Gone,

    Failed,
}

/// <summary>
/// Envoi Web Push (M1) : message chiffré (RFC 8291) et signé VAPID (RFC 8292), sans bibliothèque tierce.
/// Les adresses push sont limitées aux services connus des navigateurs, pour que l'API ne puisse pas servir à
/// appeler une adresse arbitraire du réseau interne.
/// </summary>
public sealed partial class WebPushSender : IDisposable
{
    public const string HttpClientName = "web-push";

    /// <summary>Services push des navigateurs : Chrome et Edge (FCM), Firefox, Safari, Edge historique (WNS).</summary>
    private static readonly string[] AllowedHostSuffixes =
        ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    private readonly IHttpClientFactory http;
    private readonly TimeProvider clock;
    private readonly ILogger<WebPushSender> logger;
    private readonly Vapid? vapid;

    public WebPushSender(IHttpClientFactory http, IOptions<HomeBudgetOptions> options, TimeProvider clock, ILogger<WebPushSender> logger)
    {
        this.http = http;
        this.clock = clock;
        this.logger = logger;
        var o = options.Value;
        if (o.Push.IsConfigured)
        {
            var subject = !string.IsNullOrWhiteSpace(o.Push.Subject) ? o.Push.Subject.Trim()
                : !string.IsNullOrWhiteSpace(o.PublicUrl) ? o.PublicUrl.Trim()
                : "mailto:admin@localhost";
            vapid = Vapid.Load(o.Push.VapidPublicKey!, o.Push.VapidPrivateKey!, subject);
        }
    }

    public bool IsConfigured => vapid is not null;

    public string? PublicKey => vapid?.PublicKey;

    /// <summary>Adresse push acceptable : HTTPS, port par défaut, hôte d'un service push connu.</summary>
    public static bool IsAllowedEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && AllowedHostSuffixes.Any(s => uri.Host.Equals(s, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));

    public async Task<PushOutcome> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken ct)
    {
        if (vapid is null || !IsAllowedEndpoint(subscription.Endpoint))
        {
            return PushOutcome.Failed;
        }
        var p256dh = WebPushCrypto.DecodeKey(subscription.P256dh);
        var auth = WebPushCrypto.DecodeKey(subscription.Auth);
        if (p256dh is null || auth is null)
        {
            return PushOutcome.Gone;
        }

        var endpoint = new Uri(subscription.Endpoint);
        byte[] body;
        try
        {
            body = WebPushCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), p256dh, auth);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            LogInvalidKeys(endpoint.Host);
            return PushOutcome.Gone;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization", vapid.Authorization(endpoint, clock.GetUtcNow()));
        request.Headers.Add("TTL", "86400");
        request.Headers.Add("Urgency", "normal");

        try
        {
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return PushOutcome.Sent;
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return PushOutcome.Gone;
            }
            LogRejected(endpoint.Host, (int)response.StatusCode);
            return PushOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogUnreachable(endpoint.Host, ex);
            return PushOutcome.Failed;
        }
    }

    public void Dispose() => vapid?.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification push refusée par {Host} (statut {Status}).")]
    private partial void LogRejected(string host, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Service push {Host} injoignable.")]
    private partial void LogUnreachable(string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clés d'abonnement push invalides pour {Host} : abonnement supprimé.")]
    private partial void LogInvalidKeys(string host);
}
