using System.Collections.Concurrent;
using System.Net;

using HomeBudget.Api.Infrastructure.Notifications;
using HomeBudget.Tests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Notifications;

/// <summary>Requête reçue par le faux service push.</summary>
public sealed record PushRequest(Uri Endpoint, string? Authorization, string? Ttl, string? ContentEncoding, byte[] Body);

/// <summary>Faux service push : enregistre les requêtes et répond avec le statut choisi pour chaque adresse.</summary>
public sealed class FakePushService : HttpMessageHandler
{
    public ConcurrentQueue<PushRequest> Requests { get; } = new();

    public ConcurrentDictionary<string, HttpStatusCode> Statuses { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(new PushRequest(
            request.RequestUri!,
            request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
            request.Headers.TryGetValues("TTL", out var ttl) ? ttl.Single() : null,
            request.Content?.Headers.ContentEncoding.SingleOrDefault(),
            request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
        return new HttpResponseMessage(Statuses.GetValueOrDefault(request.RequestUri!.ToString(), HttpStatusCode.Created));
    }

    // La fabrique de clients HTTP libère ses gestionnaires : celui-ci est partagé par tout le test.
    protected override void Dispose(bool disposing)
    {
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public bool Fail { get; set; }

    public bool IsConfigured => true;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP indisponible.");
        }
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Application avec clés VAPID, faux service push et faux envoi d'e-mails ; la tâche de fond est arrêtée.</summary>
public sealed class PushTestFactory : HomeBudgetFactory
{
    private static readonly (string Public, string Private) Keys = Vapid.Generate();

    public FakePushService Push { get; } = new();

    public FakeEmailSender Email { get; } = new();

    public string VapidPublicKey => Keys.Public;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("HB:Push:VapidPublicKey", Keys.Public);
        builder.UseSetting("HB:Push:VapidPrivateKey", Keys.Private);
        builder.UseSetting("HB:Push:Subject", "mailto:admin@example.test");
        builder.UseSetting("HB:PublicUrl", "https://budget.example.test");
        builder.UseSetting("HB:Notifications:Enabled", "false");
        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient(WebPushSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Push);
            s.AddSingleton<IEmailSender>(Email);
        });
    }
}
