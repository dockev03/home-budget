using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class TwoFactorTests
{
    [Fact]
    public async Task Enable_two_factor_then_login_requires_code()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var key = await EnableTwoFactorAsync(admin);

        var client = factory.CreateApiClient();
        var step1 = await ApiClient.ReadJsonAsync(await client.PostAsync("/api/auth/login",
            new { userName = TestData.AdminUserName, password = TestData.AdminPassword }));
        step1.GetProperty("status").GetString().ShouldBe("RequiresTwoFactor");
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var wrong = await client.PostAsync("/api/auth/login/two-factor", new { code = "000000" });
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var step2 = await ApiClient.ReadJsonAsync(await client.PostAsync("/api/auth/login/two-factor", new { code = Totp.Compute(key) }));
        step2.GetProperty("status").GetString().ShouldBe("Succeeded");
        step2.GetProperty("user").GetProperty("twoFactorEnabled").GetBoolean().ShouldBeTrue();
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Recovery_code_works_once()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var (_, codes) = await EnableTwoFactorWithCodesAsync(admin);
        codes.Count.ShouldBe(10);

        async Task<HttpStatusCode> LoginWithRecoveryAsync()
        {
            var client = factory.CreateApiClient();
            await client.PostAsync("/api/auth/login", new { userName = TestData.AdminUserName, password = TestData.AdminPassword });
            return (await client.PostAsync("/api/auth/login/recovery-code", new { recoveryCode = codes[0].ToLowerInvariant() })).StatusCode;
        }

        (await LoginWithRecoveryAsync()).ShouldBe(HttpStatusCode.OK);
        (await LoginWithRecoveryAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Enable_rejects_invalid_code()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await admin.PostAsync("/api/account/two-factor/setup");

        var response = await admin.PostAsync("/api/account/two-factor/enable", new { code = "123456" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Disable_requires_password()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await EnableTwoFactorAsync(admin);

        (await admin.PostAsync("/api/account/two-factor/disable", new { password = "mauvais mot de passe" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/account/two-factor/disable", new { password = TestData.AdminPassword }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var status = await ApiClient.ReadJsonAsync(await admin.GetAsync("/api/account/two-factor"));
        status.GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    private static async Task<string> EnableTwoFactorAsync(ApiClient client) => (await EnableTwoFactorWithCodesAsync(client)).Key;

    private static async Task<(string Key, IReadOnlyList<string> Codes)> EnableTwoFactorWithCodesAsync(ApiClient client)
    {
        var setup = await ApiClient.ReadJsonAsync(await client.PostAsync("/api/account/two-factor/setup"));
        var key = setup.GetProperty("sharedKey").GetString()!;
        setup.GetProperty("authenticatorUri").GetString()!.ShouldStartWith("otpauth://totp/");

        var enable = await client.PostAsync("/api/account/two-factor/enable", new { code = Totp.Compute(key) });
        enable.StatusCode.ShouldBe(HttpStatusCode.OK);
        var codes = (await ApiClient.ReadJsonAsync(enable)).GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        await client.RefreshCsrfAsync();
        return (key, codes);
    }
}
