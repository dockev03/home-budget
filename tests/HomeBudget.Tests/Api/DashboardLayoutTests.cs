using System.Net;

using HomeBudget.Api.Domain.Dashboard;
using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class DashboardLayoutTests
{
    private static List<string> Strings(System.Text.Json.JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public async Task Each_user_saves_their_own_layout_and_can_reset_it()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        var initial = await admin.GetJsonAsync("/api/dashboard/layout");
        initial.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        Strings(initial, "order").ShouldBe(DashboardLayout.DefaultOrder);
        Strings(initial, "hidden").ShouldBeEmpty();

        var saved = await admin.PutAsync("/api/dashboard/layout",
            new { order = new[] { "accounts", "unknown", "keyFigures" }, hidden = new[] { "history" } }, null);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ApiClient.ReadJsonAsync(saved);
        body.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        Strings(body, "order").Take(3).ShouldBe(["accounts", "keyFigures", "forecast"]);
        Strings(body, "order").Count.ShouldBe(DashboardLayout.DefaultOrder.Count);
        Strings(body, "hidden").ShouldBe(["history"]);

        var reread = await admin.GetJsonAsync("/api/dashboard/layout");
        Strings(reread, "order").ShouldBe(Strings(body, "order"));
        Strings(reread, "hidden").ShouldBe(["history"]);

        // Un utilisateur en lecture seule personnalise aussi son accueil, sans toucher à celui des autres.
        (await reader.GetJsonAsync("/api/dashboard/layout")).GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        var readerSave = await reader.PutAsync("/api/dashboard/layout", new { order = new[] { "wealth" }, hidden = Array.Empty<string>() }, null);
        readerSave.StatusCode.ShouldBe(HttpStatusCode.OK);
        Strings(await admin.GetJsonAsync("/api/dashboard/layout"), "order")[0].ShouldBe("accounts");

        var reset = await admin.DeleteAsync("/api/dashboard/layout", null);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterReset = await admin.GetJsonAsync("/api/dashboard/layout");
        afterReset.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        Strings(afterReset, "order").ShouldBe(DashboardLayout.DefaultOrder);
        Strings(await reader.GetJsonAsync("/api/dashboard/layout"), "order")[0].ShouldBe("wealth");
    }

    [Fact]
    public async Task An_oversized_layout_is_refused()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();

        var tooMany = await admin.PutAsync("/api/dashboard/layout",
            new { order = Enumerable.Repeat("keyFigures", DashboardLayout.MaxBlocks + 1).ToArray(), hidden = Array.Empty<string>() }, null);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tooLong = await admin.PutAsync("/api/dashboard/layout",
            new { order = new[] { new string('x', DashboardLayout.MaxBlockLength + 1) }, hidden = Array.Empty<string>() }, null);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
