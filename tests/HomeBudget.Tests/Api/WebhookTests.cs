using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Webhooks;
using HomeBudget.Tests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

/// <summary>Requête reçue par le faux destinataire des webhooks.</summary>
public sealed record WebhookRequestLog(Uri Url, IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>Faux destinataire : enregistre les requêtes et répond avec le statut choisi.</summary>
public sealed class FakeWebhookReceiver : HttpMessageHandler
{
    public ConcurrentQueue<WebhookRequestLog> Requests { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(new WebhookRequestLog(
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            await request.Content!.ReadAsStringAsync(cancellationToken)));
        return new HttpResponseMessage(Status);
    }

    protected override void Dispose(bool disposing)
    {
    }
}

/// <summary>Application dont les webhooks partent vers le faux destinataire ; la tâche de fond est arrêtée.</summary>
public sealed class WebhookTestFactory(bool fakeReceiver = true) : HomeBudgetFactory
{
    public FakeWebhookReceiver Receiver { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("HB:Webhooks:Enabled", "false");
        if (fakeReceiver)
        {
            builder.ConfigureTestServices(s => s.AddHttpClient(WebhookSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Receiver));
        }
    }

    public async Task<int> DispatchAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>().DispatchDueAsync(CancellationToken.None);
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>Webhooks signés de l'API publique (I5).</summary>
public sealed class WebhookTests
{
    private static readonly string[] TransactionEvents = ["transaction.created", "transaction.updated", "transaction.deleted"];

    private static async Task<(Guid Id, string Secret)> CreateWebhookAsync(ApiClient admin, string[]? events = null, string url = "https://hooks.example.test/budget")
    {
        var created = await admin.CreateAsync("/api/webhooks", new { name = "Domotique", url, events = events ?? TransactionEvents });
        return (created.GetProperty("item").GetProperty("id").GetGuid(), created.GetProperty("secret").GetString()!);
    }

    [Fact]
    public async Task Transaction_events_are_queued_signed_and_delivered()
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 100m);
        var (_, secret) = await CreateWebhookAsync(admin);
        secret.ShouldStartWith("whsec_");

        var created = await admin.CreateTransactionAsync(account, -42.5m, label: "Boulangerie");
        var id = created.GetProperty("id").GetGuid();
        var cleared = await admin.PostAsync($"/api/transactions/{id}/clear", new { isCleared = true });
        cleared.EnsureSuccessStatusCode();
        var version = (await ApiClient.ReadJsonAsync(cleared)).Version();
        (await admin.DeleteAsync($"/api/transactions/{id}", version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await factory.DispatchAsync()).ShouldBe(3);
        var requests = factory.Receiver.Requests.ToList();
        requests.Select(r => r.Headers["X-HomeBudget-Event"]).ShouldBe(TransactionEvents);
        foreach (var request in requests)
        {
            request.Url.ShouldBe(new Uri("https://hooks.example.test/budget"));
            var timestamp = long.Parse(request.Headers["X-HomeBudget-Timestamp"], System.Globalization.CultureInfo.InvariantCulture);
            request.Headers["X-HomeBudget-Signature"].ShouldBe(WebhookSigner.Sign(secret, timestamp, request.Body));
            using var json = JsonDocument.Parse(request.Body);
            json.RootElement.GetProperty("id").GetString().ShouldBe(request.Headers["X-HomeBudget-Delivery"]);
            var transaction = json.RootElement.GetProperty("data").GetProperty("transactions")[0];
            transaction.GetProperty("id").GetGuid().ShouldBe(id);
            transaction.GetProperty("amount").GetDecimal().ShouldBe(-42.5m);
        }
        using (var updated = JsonDocument.Parse(requests[1].Body))
        {
            updated.RootElement.GetProperty("data").GetProperty("transactions")[0].GetProperty("isCleared").GetBoolean().ShouldBeTrue();
        }

        // Déjà envoyés : rien de plus au passage suivant.
        (await factory.DispatchAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Webhook_only_receives_what_its_owner_sees_and_subscribed_events()
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var shared = await admin.CreateAccountAsync("Commun");
        var mine = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso Alice",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(TestScenario.Today.AddYears(-1)),
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        await CreateWebhookAsync(admin, ["transaction.created"]);

        await alice.CreateTransactionAsync(mine, -99m, label: "Secret");
        await alice.CreateTransactionAsync(shared, -10m, label: "Courses communes");
        var transfer = await alice.CreateAsync("/api/transfers", new
        {
            fromAccountId = shared,
            toAccountId = mine,
            date = TestScenario.Iso(TestScenario.Today),
            amount = 50m,
        });
        transfer.GetProperty("transferId").GetGuid().ShouldNotBe(Guid.Empty);

        await factory.DispatchAsync();
        var labels = factory.Receiver.Requests
            .SelectMany(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("data").GetProperty("transactions").EnumerateArray())
            .Select(t => t.GetProperty("accountId").GetGuid())
            .ToList();
        // L'opération du compte privé et la moitié privée du virement n'apparaissent pas.
        labels.ShouldBe([shared, shared]);
    }

    [Fact]
    public async Task Failed_deliveries_are_retried_then_abandoned_and_the_webhook_disabled()
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var (webhookId, _) = await CreateWebhookAsync(admin, ["transaction.created"]);
        factory.Receiver.Status = HttpStatusCode.InternalServerError;
        await admin.CreateTransactionAsync(account, -1m);

        await factory.DispatchAsync();
        var history = await admin.GetJsonAsync($"/api/webhooks/{webhookId}/deliveries");
        history[0].GetProperty("status").GetString().ShouldBe("Pending");
        history[0].GetProperty("attempts").GetInt32().ShouldBe(1);
        history[0].GetProperty("statusCode").GetInt32().ShouldBe(500);
        // Prochaine tentative dans une minute : rien n'est dû tout de suite.
        (await factory.DispatchAsync()).ShouldBe(0);

        // Quatre envois déjà abandonnés : le prochain abandon désactive le webhook.
        await factory.WithDbAsync(db => db.Webhooks.ExecuteUpdateAsync(s => s.SetProperty(w => w.ConsecutiveFailures, 4)));
        for (var attempt = 2; attempt <= 6; attempt++)
        {
            await factory.WithDbAsync(db => db.WebhookDeliveries.ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAt, DateTimeOffset.UtcNow.AddDays(-1))));
            (await factory.DispatchAsync()).ShouldBe(1);
        }
        factory.Receiver.Requests.Count.ShouldBe(6);
        history = await admin.GetJsonAsync($"/api/webhooks/{webhookId}/deliveries");
        history[0].GetProperty("status").GetString().ShouldBe("Failed");

        var webhook = (await admin.GetJsonAsync("/api/webhooks"))[0];
        webhook.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        var audit = await admin.GetJsonAsync("/api/audit?action=WebhookDisabled");
        audit.GetProperty("items").GetArrayLength().ShouldBe(1);

        // Inactif : plus aucun événement en file ; le réactiver remet le compteur à zéro.
        await admin.CreateTransactionAsync(account, -2m);
        (await factory.DispatchAsync()).ShouldBe(0);
        var reactivated = await admin.PutAsync($"/api/webhooks/{webhookId}", new
        {
            name = "Domotique",
            url = "https://hooks.example.test/budget",
            events = new[] { "transaction.created" },
            isActive = true,
        }, webhook.Version());
        reactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiClient.ReadJsonAsync(reactivated)).GetProperty("consecutiveFailures").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("ftp://example.test/hook")]
    [InlineData("pas une adresse")]
    [InlineData("http://127.0.0.1:8080/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/hook")]
    [InlineData("https://user:password@example.test/hook")]
    public async Task Dangerous_or_invalid_addresses_are_refused(string url)
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsync("/api/webhooks", new { name = "x", url, events = TransactionEvents });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("url", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Webhooks_are_reserved_to_admins_and_validated()
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var member = await factory.CreateUserClientAsync("membre", "Member");
        (await member.GetAsync("/api/webhooks")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PostAsync("/api/webhooks", new { name = "x", url = "https://example.test", events = TransactionEvents }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await admin.PostAsync("/api/webhooks", new { name = "x", url = "https://example.test", events = new[] { "account.deleted" } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Réseau privé (domotique, réseau Docker) : autorisé par défaut.
        (await admin.PostAsync("/api/webhooks", new { name = "n8n", url = "http://192.168.1.20:5678/webhook", events = TransactionEvents }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await admin.GetJsonAsync("/api/webhooks/events")).GetArrayLength().ShouldBe(4);
    }

    [Fact]
    public async Task Test_ping_and_secret_regeneration()
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var (id, secret) = await CreateWebhookAsync(admin);

        var ping = await admin.CreateAsync($"/api/webhooks/{id}/test", new { });
        ping.GetProperty("status").GetString().ShouldBe("Succeeded");
        ping.GetProperty("statusCode").GetInt32().ShouldBe(200);
        var request = factory.Receiver.Requests.Single();
        request.Headers["X-HomeBudget-Event"].ShouldBe("ping");
        var timestamp = long.Parse(request.Headers["X-HomeBudget-Timestamp"], System.Globalization.CultureInfo.InvariantCulture);
        request.Headers["X-HomeBudget-Signature"].ShouldBe(WebhookSigner.Sign(secret, timestamp, request.Body));

        var regenerated = (await admin.CreateAsync($"/api/webhooks/{id}/secret", new { })).GetProperty("secret").GetString()!;
        regenerated.ShouldNotBe(secret);
        await admin.CreateAsync($"/api/webhooks/{id}/test", new { });
        var second = factory.Receiver.Requests.Last();
        var secondTimestamp = long.Parse(second.Headers["X-HomeBudget-Timestamp"], System.Globalization.CultureInfo.InvariantCulture);
        second.Headers["X-HomeBudget-Signature"].ShouldBe(WebhookSigner.Sign(regenerated, secondTimestamp, second.Body));

        // Le secret n'est jamais renvoyé ensuite, et il est chiffré en base.
        (await admin.GetJsonAsync("/api/webhooks"))[0].TryGetProperty("secret", out _).ShouldBeFalse();
        await factory.WithDbAsync(async db =>
            (await db.Webhooks.SingleAsync()).ProtectedSecret.ShouldNotContain(regenerated));
    }

    [Fact]
    public async Task Connection_to_a_denied_address_is_refused_after_name_resolution()
    {
        await using var factory = new WebhookTestFactory(fakeReceiver: false);
        var admin = await factory.CreateAdminClientAsync();
        var (id, _) = await CreateWebhookAsync(admin, url: "https://hooks.example.test/budget");
        // Un nom qui désigne la boucle locale (rebinding DNS) : refusé à la connexion.
        await factory.WithDbAsync(db => db.Webhooks.ExecuteUpdateAsync(s => s.SetProperty(w => w.Url, "http://localhost:9/hook")));

        var ping = await admin.CreateAsync($"/api/webhooks/{id}/test", new { });
        ping.GetProperty("status").GetString().ShouldBe("Failed");
        ping.GetProperty("error").GetString()!.ShouldContain("Adresse refusée");
    }

    [Fact]
    public async Task Import_completed_event_is_sent()
    {
        await using var factory = new WebhookTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        await CreateWebhookAsync(admin, [WebhookEvents.ImportCompleted]);
        await factory.WithDbAsync(async db =>
        {
            db.ImportBatches.Add(new HomeBudget.Api.Domain.Imports.ImportBatch { AccountId = account, FileName = "releve.csv", Created = 3 });
            await db.SaveChangesAsync();
        });

        await factory.DispatchAsync();
        var request = factory.Receiver.Requests.Single();
        request.Headers["X-HomeBudget-Event"].ShouldBe("import.completed");
        using var json = JsonDocument.Parse(request.Body);
        json.RootElement.GetProperty("data").GetProperty("fileName").GetString().ShouldBe("releve.csv");
        json.RootElement.GetProperty("data").GetProperty("created").GetInt32().ShouldBe(3);
    }
}
