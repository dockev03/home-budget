using System.Net.Http.Headers;
using System.Text;

using HomeBudget.Api.Domain.Integrations;

namespace HomeBudget.Api.Infrastructure.Webhooks;

/// <summary>Résultat d'un envoi : code HTTP reçu, ou erreur de connexion.</summary>
public sealed record WebhookSendResult(bool Success, int? StatusCode, string? Error);

/// <summary>
/// Envoi HTTP d'un événement (I5) : POST JSON signé, délai de 10 s, sans suivre les redirections, sans proxy, et
/// seulement vers les adresses autorisées (<see cref="WebhookAddressPolicy"/>). La réponse n'est pas lue : seul le
/// code compte (2xx = reçu).
/// </summary>
public sealed class WebhookSender(IHttpClientFactory http, WebhookSecrets secrets, TimeProvider clock)
{
    public const string HttpClientName = "webhooks";

    public async Task<WebhookSendResult> SendAsync(Webhook webhook, WebhookDelivery delivery, CancellationToken ct)
    {
        try
        {
            var secret = secrets.Unprotect(webhook.ProtectedSecret);
            var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HomeBudget-Webhooks", "1.0"));
            request.Headers.Add(WebhookSigner.EventHeader, delivery.Event);
            request.Headers.Add(WebhookSigner.DeliveryHeader, delivery.Id.ToString());
            request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(secret, timestamp, delivery.Payload));

            using var response = await http.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, code, null)
                : new WebhookSendResult(false, code, $"Réponse HTTP {code}.");
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.InnerException is WebhookAddressException address ? address.Message : ex.Message;
            return new WebhookSendResult(false, null, Truncate(reason));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, "Délai dépassé (10 s).");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new WebhookSendResult(false, null, "Secret illisible (clés de protection perdues) : régénérez-le.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= WebhookDelivery.MaxErrorLength ? value : value[..WebhookDelivery.MaxErrorLength];
}
