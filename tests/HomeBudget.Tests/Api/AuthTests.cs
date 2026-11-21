using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class AuthTests
{
    [Fact]
    public async Task Me_without_session_is_unauthorized()
    {
        await using var factory = new HomeBudgetFactory();

        var response = await factory.CreateApiClient().GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_without_csrf_token_is_rejected()
    {
        await using var factory = new HomeBudgetFactory();

        var response = await factory.CreateApiClient()
            .PostAsync("/api/auth/login", new { userName = "admin", password = "x" }, withCsrf: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("anti-CSRF");
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();

        var response = await factory.CreateApiClient()
            .PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = "mauvais mot de passe" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_then_logout()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var client = factory.CreateApiClient();

        var login = await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = TestData.AdminPassword });
        var json = await ApiClient.ReadJsonAsync(login);
        json.GetProperty("status").GetString().ShouldBe("Succeeded");
        json.GetProperty("user").GetProperty("userName").GetString().ShouldBe(TestData.AdminUserName);
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Le jeton anti-CSRF est lié à l'utilisateur : il faut le renouveler après connexion.
        await client.RefreshCsrfAsync();
        (await client.PostAsync("/api/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Account_is_locked_after_five_failures()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var client = factory.CreateApiClient();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = "mauvais mot de passe" });
        }
        var response = await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = TestData.AdminPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task Session_cookie_is_secure_httponly_and_strict()
    {
        await using var factory = new HomeBudgetFactory();
        var client = factory.CreateApiClient();
        await factory.CreateAdminClientAsync();

        var response = await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = TestData.AdminPassword });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("hb_session=", StringComparison.Ordinal));
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
    }
}
