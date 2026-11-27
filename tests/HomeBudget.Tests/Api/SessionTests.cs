using System.Net;
using System.Text.Json;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Tests.Infrastructure;
using HomeBudget.Tests.Notifications;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HomeBudget.Tests.Api;

/// <summary>Sessions ouvertes et révocation (N8), alertes de sécurité par e-mail (N9).</summary>
public sealed class SessionTests
{
    private const string FirefoxWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0";
    private const string SafariIphone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    private static async Task<ApiClient> LoginAsync(HomeBudgetFactory factory, string userAgent, string userName = TestData.AdminUserName)
    {
        var client = factory.CreateApiClient();
        client.Http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var login = await client.PostAsync("/api/auth/login", new { userName, password = TestData.AdminPassword });
        login.EnsureSuccessStatusCode();
        await client.RefreshCsrfAsync();
        return client;
    }

    private static async Task<List<JsonElement>> SessionsAsync(ApiClient client) =>
        [.. (await client.GetJsonAsync("/api/account/sessions")).EnumerateArray()];

    private static async Task<List<string>> WaitForEmailsAsync(FakeEmailSender email, int count)
    {
        for (var i = 0; i < 100 && email.Sent.Count < count; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        return [.. email.Sent.Select(m => m.Subject)];
    }

    [Fact]
    public async Task Sessions_are_listed_with_device_and_current_flag()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var pc = await LoginAsync(factory, FirefoxWindows);
        await LoginAsync(factory, SafariIphone);

        var sessions = await SessionsAsync(pc);

        // Installation, PC et téléphone.
        sessions.Count.ShouldBe(3);
        var current = sessions.Single(s => s.GetProperty("isCurrent").GetBoolean());
        current.GetProperty("browser").GetString().ShouldBe("Firefox 133");
        current.GetProperty("operatingSystem").GetString().ShouldBe("Windows");
        current.GetProperty("device").GetString().ShouldBe("Desktop");
        current.GetProperty("origin").GetString().ShouldBe("Password");
        var phone = sessions.Single(s => s.GetProperty("browser").GetString() == "Safari 17");
        phone.GetProperty("operatingSystem").GetString().ShouldBe("iOS 17");
        phone.GetProperty("device").GetString().ShouldBe("Mobile");
    }

    [Fact]
    public async Task Revoked_session_is_refused_at_next_request_and_audited()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var pc = await LoginAsync(factory, FirefoxWindows);
        var phone = await LoginAsync(factory, SafariIphone);
        var phoneId = (await SessionsAsync(phone)).Single(s => s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetString();

        (await pc.DeleteAsync($"/api/account/sessions/{phoneId}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await phone.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await pc.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SessionsAsync(pc)).ShouldNotContain(s => s.GetProperty("id").GetString() == phoneId);
        var audit = await pc.GetJsonAsync("/api/audit?action=SessionRevoked");
        audit.GetRawText().ShouldContain("Safari 17");
    }

    [Fact]
    public async Task Revoke_others_keeps_only_the_current_session()
    {
        await using var factory = new HomeBudgetFactory();
        var first = await factory.CreateAdminClientAsync();
        var pc = await LoginAsync(factory, FirefoxWindows);
        var phone = await LoginAsync(factory, SafariIphone);

        var result = await pc.CreateAsync("/api/account/sessions/revoke-others", new { });

        result.GetProperty("revoked").GetInt32().ShouldBe(2);
        (await first.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await phone.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionsAsync(pc)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Logout_closes_the_session_even_for_a_copy_of_the_cookie()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var pc = factory.CreateApiClient();
        var login = await pc.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = TestData.AdminPassword });
        login.EnsureSuccessStatusCode();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(HomeBudgetServices.CookieName + "=", StringComparison.Ordinal));
        await pc.RefreshCsrfAsync();

        (await pc.PostAsync("/api/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SessionsAsync(admin)).Count.ShouldBe(1);
        var copy = factory.CreateApiClient();
        copy.Http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        (await copy.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_change_keeps_the_current_session_and_drops_the_others_from_the_list()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var pc = await LoginAsync(factory, FirefoxWindows);
        var before = (await SessionsAsync(pc)).Single(s => s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetString();

        (await pc.PostAsync("/api/account/password", new { currentPassword = TestData.AdminPassword, newPassword = "une toute nouvelle phrase" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var sessions = await SessionsAsync(pc);
        sessions.Count.ShouldBe(1);
        sessions[0].GetProperty("id").GetString().ShouldBe(before);
        (await admin.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_user_cannot_see_or_revoke_the_sessions_of_another()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var member = await factory.CreateUserClientAsync("membre", Roles.Member);
        var adminSession = (await SessionsAsync(admin)).Single().GetProperty("id").GetString();

        (await SessionsAsync(member)).Count.ShouldBe(1);
        (await member.DeleteAsync($"/api/account/sessions/{adminSession}", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_alert_is_sent_only_when_enabled()
    {
        await using var factory = new PushTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var settings = await admin.GetJsonAsync("/api/account/security-alerts");
        settings.GetProperty("onLogin").GetBoolean().ShouldBeFalse();
        settings.GetProperty("onLockout").GetBoolean().ShouldBeTrue();

        await LoginAsync(factory, FirefoxWindows);
        (await WaitForEmailsAsync(factory.Email, 1)).ShouldBeEmpty();

        (await admin.PutAsync("/api/account/security-alerts", new { onLogin = true, onLockout = true }, null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await LoginAsync(factory, SafariIphone);

        (await WaitForEmailsAsync(factory.Email, 1)).ShouldBe(["Home Budget · nouvelle connexion à votre compte"]);
        var message = factory.Email.Sent.Single();
        message.To.ShouldBe("admin@example.test");
        message.Text.ShouldContain("Safari 17");
        message.Text.ShouldContain("iOS 17 (mobile)");
        message.Text.ShouldContain("Méthode : mot de passe");
        message.Text.ShouldContain("https://budget.example.test/securite");
    }

    [Fact]
    public async Task Lockout_alert_is_sent_once_per_lockout()
    {
        await using var factory = new PushTestFactory();
        await factory.CreateAdminClientAsync();
        var client = factory.CreateApiClient();

        for (var i = 0; i < 7; i++)
        {
            await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = "mauvais mot de passe" });
        }

        (await WaitForEmailsAsync(factory.Email, 2)).ShouldBe(["Home Budget · compte verrouillé après plusieurs échecs de connexion"]);
        factory.Email.Sent.Single().Text.ShouldContain("verrouillé jusqu'à");
    }

    [Fact]
    public async Task Lockout_alert_can_be_disabled()
    {
        await using var factory = new PushTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        (await admin.PutAsync("/api/account/security-alerts", new { onLogin = false, onLockout = false }, null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var client = factory.CreateApiClient();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = "mauvais mot de passe" });
        }

        (await WaitForEmailsAsync(factory.Email, 1)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Login_alert_requires_an_email_address()
    {
        await using var factory = new PushTestFactory();
        await factory.CreateAdminClientAsync();
        var member = await factory.CreateUserClientAsync("membre", Roles.Member);

        var response = await member.PutAsync("/api/account/security-alerts", new { onLogin = true, onLockout = true }, null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cookie_issued_before_session_tracking_is_adopted_without_logout()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        string legacyCookie;
        using (var scope = factory.Services.CreateScope())
        {
            // Cookie tel qu'émis par la version précédente : sans revendication de session.
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
            var user = await signIn.UserManager.FindByNameAsync(TestData.AdminUserName);
            var principal = await signIn.CreateUserPrincipalAsync(user!);
            var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);
            var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow.AddDays(-2), ExpiresUtc = DateTimeOffset.UtcNow.AddDays(28) };
            legacyCookie = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme));
        }
        var legacy = factory.CreateApiClient();
        legacy.Http.DefaultRequestHeaders.Add("Cookie", $"{HomeBudgetServices.CookieName}={legacyCookie}");

        var me = await legacy.GetAsync("/api/auth/me");

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        me.Headers.TryGetValues("Set-Cookie", out var renewed).ShouldBeTrue();
        renewed!.ShouldContain(c => c.StartsWith(HomeBudgetServices.CookieName + "=", StringComparison.Ordinal));
        // Les requêtes parallèles d'une même page reprennent la même session.
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => legacy.GetAsync("/api/auth/me")));
        var sessions = await SessionsAsync(admin);
        sessions.Count.ShouldBe(2);
        var adopted = sessions.Single(s => s.GetProperty("origin").GetString() == "Legacy").GetProperty("id").GetString();

        // Révoquée, la session reste refusée, même à une copie du cookie d'origine.
        (await admin.DeleteAsync($"/api/account/sessions/{adopted}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var copy = factory.CreateApiClient();
        copy.Http.DefaultRequestHeaders.Add("Cookie", $"{HomeBudgetServices.CookieName}={legacyCookie}");
        (await copy.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionsAsync(admin)).Count.ShouldBe(1);
    }
}
