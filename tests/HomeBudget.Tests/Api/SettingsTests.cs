using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class SettingsTests
{
    private static object Request(string mode = "Calendar", int day = 1, int warning = 80, int alert = 100) =>
        new { periodMode = mode, periodStartDay = day, budgetWarningPercent = warning, budgetAlertPercent = alert };

    [Fact]
    public async Task Defaults_are_calendar_period_and_80_100_thresholds()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();

        var settings = await admin.GetJsonAsync("/api/settings");

        settings.GetProperty("periodMode").GetString().ShouldBe("Calendar");
        settings.GetProperty("budgetWarningPercent").GetInt32().ShouldBe(80);
        settings.GetProperty("budgetAlertPercent").GetInt32().ShouldBe(100);
    }

    [Fact]
    public async Task Admin_changes_period_and_current_period_follows()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetJsonAsync("/api/settings")).Version();

        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync("/api/settings", Request("FixedDay", 25), version));
        updated.Version().ShouldBe(version + 1);

        var current = await admin.GetJsonAsync("/api/periods/current");
        current.GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
        DateOnly.Parse(current.GetProperty("start").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Day.ShouldBe(25);
        (await admin.GetJsonAsync("/api/periods/2026-11")).GetProperty("start").GetString().ShouldBe("2026-10-25");
        (await admin.GetAsync("/api/periods/novembre")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetJsonAsync("/api/settings")).Version();

        (await admin.PutAsync("/api/settings", Request("FixedDay", 29), version)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/settings", Request(warning: 100, alert: 100), version)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/settings", Request(), null)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await admin.PutAsync("/api/settings", Request(), version + 5)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Only_admins_change_settings()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var member = await factory.CreateUserClientAsync("membre", "Member");

        (await member.GetAsync("/api/settings")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.PutAsync("/api/settings", Request(), 0)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
