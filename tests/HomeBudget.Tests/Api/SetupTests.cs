using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class SetupTests
{
    [Fact]
    public async Task Status_is_required_while_no_user_exists()
    {
        await using var factory = new HomeBudgetFactory();
        var client = factory.CreateApiClient();

        var json = await ApiClient.ReadJsonAsync(await client.GetAsync("/api/setup/status"));

        json.GetProperty("required").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Setup_with_invalid_token_is_forbidden()
    {
        await using var factory = new HomeBudgetFactory();
        var client = factory.CreateApiClient();

        var response = await client.PostAsync("/api/setup", new { token = "wrong", userName = "admin", password = TestData.AdminPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.SetupTokens.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Setup_rejects_short_password_with_french_message()
    {
        await using var factory = new HomeBudgetFactory();
        var client = factory.CreateApiClient();

        var response = await client.PostAsync("/api/setup", new { token = TestData.SetupToken, userName = "admin", password = "trop court" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("au moins 12 caractères");
    }

    [Fact]
    public async Task Setup_creates_admin_signs_in_and_closes_setup()
    {
        await using var factory = new HomeBudgetFactory();
        var client = await factory.CreateAdminClientAsync();

        var me = await ApiClient.ReadJsonAsync(await client.GetAsync("/api/auth/me"));
        me.GetProperty("userName").GetString().ShouldBe(TestData.AdminUserName);
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("Admin");

        var status = await ApiClient.ReadJsonAsync(await client.GetAsync("/api/setup/status"));
        status.GetProperty("required").GetBoolean().ShouldBeFalse();

        var second = await factory.CreateApiClient()
            .PostAsync("/api/setup", new { token = TestData.SetupToken, userName = "intrus", password = TestData.AdminPassword });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
