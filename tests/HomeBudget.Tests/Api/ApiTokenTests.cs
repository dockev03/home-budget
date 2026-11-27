using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Tests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

/// <summary>Jetons d'accès personnels de l'API publique (I5).</summary>
public sealed class ApiTokenTests
{
    private sealed class LimitedFactory : HomeBudgetFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("HB:Security:ApiRequestsPerMinute", "3");
        }
    }

    private static async Task<string> CreateTokenAsync(ApiClient client, string scope, string name = "Script", int days = 30)
    {
        var created = await client.CreateAsync("/api/tokens", new { name, scope, expiresInDays = days });
        created.GetProperty("item").GetProperty("scope").GetString().ShouldBe(scope);
        return created.GetProperty("token").GetString()!;
    }

    /// <summary>Client sans cookie ni jeton anti-CSRF : seulement l'en-tête Authorization.</summary>
    private static HttpClient Bearer(HomeBudgetFactory factory, string token)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object Transaction(Guid accountId) => new
    {
        accountId,
        date = TestScenario.Iso(TestScenario.Today),
        label = "Depuis un script",
        lines = new[] { new { categoryId = (Guid?)null, amount = -12.5m, memo = (string?)null } },
    };

    [Fact]
    public async Task Read_token_reads_but_never_writes()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 100m);
        var token = await CreateTokenAsync(admin, "Read");
        token.ShouldStartWith("hb_");
        using var api = Bearer(factory, token);

        (await api.GetAsync(new Uri("/api/accounts", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.PostAsJsonAsync("/api/transactions", Transaction(account), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var listed = (await admin.GetJsonAsync("/api/tokens"))[0];
        listed.GetProperty("prefix").GetString().ShouldBe(token[..10]);
        listed.GetProperty("lastUsedAt").ValueKind.ShouldBe(JsonValueKind.String);
        listed.TryGetProperty("hash", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Write_token_writes_without_csrf_as_its_user_but_never_as_admin()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 100m);
        using var api = Bearer(factory, await CreateTokenAsync(admin, "Write"));

        var created = await api.PostAsJsonAsync("/api/transactions", Transaction(account), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var me = await admin.GetJsonAsync("/api/auth/me");
        (await ApiClient.ReadJsonAsync(created)).GetProperty("createdBy").GetGuid().ShouldBe(me.GetProperty("id").GetGuid());

        // Jamais d'administration, même avec le jeton d'un administrateur.
        (await api.PutAsJsonAsync("/api/settings", new { }, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreach (var url in new[] { "/api/users", "/api/backups/export", "/api/webhooks", "/api/audit", "/api/tokens", "/api/auth/me", "/api/docs/" })
        {
            (await api.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
        }
        // Un jeton ne crée pas d'autre jeton.
        (await api.PostAsJsonAsync("/api/tokens", new { name = "x", scope = "Write", expiresInDays = 30 }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_expired_invalid_and_deactivated_tokens_are_refused()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var bob = await factory.CreateUserClientAsync("bob", "Member");
        var revoked = await CreateTokenAsync(admin, "Read", "Révoqué");
        var expired = await CreateTokenAsync(admin, "Read", "Expiré");
        var bobs = await CreateTokenAsync(bob, "Read", "Bob");

        var tokens = await admin.GetJsonAsync("/api/tokens");
        var revokedId = tokens.EnumerateArray().First(t => t.GetProperty("name").GetString() == "Révoqué").GetProperty("id").GetGuid();
        (await admin.DeleteAsync($"/api/tokens/{revokedId}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ApiTokens.Where(t => t.Name == "Expiré")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), TestContext.Current.CancellationToken);
        }
        var bobId = (await bob.GetJsonAsync("/api/auth/me")).GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/users/{bobId}/status", new { isActive = false }, null)).EnsureSuccessStatusCode();

        foreach (var token in new[] { revoked, expired, bobs, "hb_inconnu", "pas-un-jeton" })
        {
            using var api = Bearer(factory, token);
            var response = await api.GetAsync(new Uri("/api/accounts", UriKind.Relative), TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, token);
            response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
        }

        // Un jeton invalide n'est pas complété par le cookie de session.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "hb_inconnu");
        (await admin.Http.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_sees_only_what_its_user_sees_and_read_only_users_get_read_tokens()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso Alice",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(TestScenario.Today.AddYears(-1)),
            isPrivate = true,
        });
        await admin.CreateAccountAsync("Commun");

        using var api = Bearer(factory, await CreateTokenAsync(admin, "Read"));
        var accounts = await (await api.GetAsync(new Uri("/api/accounts", UriKind.Relative), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        accounts.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ShouldBe(["Commun"]);

        (await reader.PostAsync("/api/tokens", new { name = "x", scope = "Write", expiresInDays = 30 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PostAsync("/api/tokens", new { name = "x", scope = "Read", expiresInDays = 7 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await CreateTokenAsync(reader, "Read");

        // Chacun ne voit et ne révoque que ses jetons.
        var readerToken = (await reader.GetJsonAsync("/api/tokens"))[0].GetProperty("id").GetGuid();
        (await admin.DeleteAsync($"/api/tokens/{readerToken}", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetJsonAsync("/api/tokens")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Requests_are_rate_limited_per_token()
    {
        await using var factory = new LimitedFactory();
        var admin = await factory.CreateAdminClientAsync();
        using var api = Bearer(factory, await CreateTokenAsync(admin, "Read"));
        for (var i = 0; i < 3; i++)
        {
            (await api.GetAsync(new Uri("/api/accounts", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        (await api.GetAsync(new Uri("/api/accounts", UriKind.Relative), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // La session de l'application n'est pas concernée.
        (await admin.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
