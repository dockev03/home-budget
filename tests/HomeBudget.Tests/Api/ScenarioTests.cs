using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Scénarios « et si… » (F5) : gestion, validation, droits et visibilité (R8).</summary>
public sealed class ScenarioTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static object Event(Guid accountId, decimal amount = -100m, string frequency = "Once", DateOnly? endDate = null, Guid? categoryId = null) => new
    {
        label = "Achat",
        accountId,
        categoryId,
        amount,
        date = TestScenario.Iso(Today.AddDays(5)),
        frequency,
        endDate = endDate is { } end ? TestScenario.Iso(end) : null,
    };

    [Fact]
    public async Task Scenarios_and_their_events_are_created_changed_and_deleted()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");

        var scenario = await admin.CreateAsync("/api/scenarios", new { name = "  Vacances  ", isActive = false });
        scenario.GetProperty("name").GetString().ShouldBe("Vacances");
        var added = await admin.CreateAsync($"/api/scenarios/{Id(scenario)}/events", Event(checking, -800m, "Once", Today.AddYears(1), groceries));
        // La date de fin ne vaut que pour un événement mensuel.
        added.GetProperty("endDate").ValueKind.ShouldBe(JsonValueKind.Null);
        added.GetProperty("categoryId").GetGuid().ShouldBe(groceries);

        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync(
            $"/api/scenarios/{Id(scenario)}/events/{Id(added)}", Event(checking, -50m, "Monthly", Today.AddMonths(6)), added.Version()));
        updated.GetProperty("frequency").GetString().ShouldBe("Monthly");
        updated.Decimal("amount").ShouldBe(-50m);
        (await admin.PutAsync($"/api/scenarios/{Id(scenario)}/events/{Id(added)}", Event(checking), added.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var renamed = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/scenarios/{Id(scenario)}", new { name = "Été", isActive = true }, scenario.Version()));
        renamed.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        renamed.GetProperty("events").GetArrayLength().ShouldBe(1);

        var list = await admin.GetJsonAsync("/api/scenarios");
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("events")[0].GetProperty("label").GetString().ShouldBe("Achat");

        // Les scénarios ne touchent pas aux données réelles.
        (await admin.GetJsonAsync("/api/transactions")).GetProperty("items").GetArrayLength().ShouldBe(0);

        (await admin.DeleteAsync($"/api/scenarios/{Id(scenario)}/events/{Id(added)}", updated.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/scenarios/{Id(scenario)}", renamed.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync("/api/scenarios")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_events_are_rejected()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var scenario = Id(await admin.CreateAsync("/api/scenarios", new { name = "Test", isActive = true }));

        async Task<JsonElement> ErrorsAsync(object body)
        {
            var response = await admin.PostAsync($"/api/scenarios/{scenario}/events", body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await ApiClient.ReadJsonAsync(response)).GetProperty("errors");
        }

        (await ErrorsAsync(Event(checking, 0m))).TryGetProperty("amount", out _).ShouldBeTrue();
        (await ErrorsAsync(Event(checking, -1.234m))).TryGetProperty("amount", out _).ShouldBeTrue();
        (await ErrorsAsync(Event(Guid.NewGuid()))).TryGetProperty("accountId", out _).ShouldBeTrue();
        (await ErrorsAsync(Event(checking, categoryId: Guid.NewGuid()))).TryGetProperty("categoryId", out _).ShouldBeTrue();
        (await ErrorsAsync(Event(checking, frequency: "Monthly", endDate: Today))).TryGetProperty("endDate", out _).ShouldBeTrue();
        (await admin.PostAsync("/api/scenarios", new { name = " ", isActive = true })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync($"/api/scenarios/{Guid.NewGuid()}/events", Event(checking))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Read_only_users_see_scenarios_but_cannot_change_them()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var scenario = Id(await admin.CreateAsync("/api/scenarios", new { name = "Test", isActive = true }));
        await admin.CreateAsync($"/api/scenarios/{scenario}/events", Event(checking));

        (await reader.GetJsonAsync("/api/scenarios")).GetArrayLength().ShouldBe(1);
        // La simulation reste possible en lecture seule : elle ne modifie rien.
        (await reader.GetJsonAsync($"/api/forecast?scenarios={scenario}")).GetProperty("scenario").Decimal("impact").ShouldBe(-100m);
        (await reader.PostAsync("/api/scenarios", new { name = "Non", isActive = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync($"/api/scenarios/{scenario}/events", Event(checking))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Events_on_a_private_account_stay_invisible_to_the_other_members()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var shared = await admin.CreateAccountAsync("Commun", 1000m, initialDate: Opening);
        var mine = Id(await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso Alice",
            type = "Checking",
            initialBalance = 500m,
            initialDate = TestScenario.Iso(Opening),
            isPrivate = true,
        }));
        var scenario = Id(await admin.CreateAsync("/api/scenarios", new { name = "Commun", isActive = true }));
        await admin.CreateAsync($"/api/scenarios/{scenario}/events", Event(shared, -100m));
        var secret = await alice.CreateAsync($"/api/scenarios/{scenario}/events", Event(mine, -42m));

        var adminView = (await admin.GetJsonAsync("/api/scenarios"))[0].GetProperty("events");
        adminView.GetArrayLength().ShouldBe(1);
        adminView[0].GetProperty("accountId").GetGuid().ShouldBe(shared);
        (await admin.GetJsonAsync($"/api/forecast?scenarios={scenario}")).GetProperty("scenario").Decimal("impact").ShouldBe(-100m);
        (await alice.GetJsonAsync($"/api/forecast?scenarios={scenario}")).GetProperty("scenario").Decimal("impact").ShouldBe(-142m);

        // Ni lecture ni écriture sur l'événement ou le compte privé d'un autre membre.
        (await admin.PutAsync($"/api/scenarios/{scenario}/events/{Id(secret)}", Event(shared), secret.Version())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/scenarios/{scenario}/events/{Id(secret)}", secret.Version())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var refused = await admin.PostAsync($"/api/scenarios/{scenario}/events", Event(mine));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(refused)).GetProperty("errors").TryGetProperty("accountId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_fictitious_events()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var project = await admin.CreateAccountAsync("Projet", 0m, initialDate: Opening);
        var scenario = Id(await admin.CreateAsync("/api/scenarios", new { name = "Test", isActive = true }));
        await admin.CreateAsync($"/api/scenarios/{scenario}/events", Event(checking));
        await admin.CreateAsync($"/api/scenarios/{scenario}/events", Event(project));

        var account = await admin.GetJsonAsync($"/api/accounts/{project}");
        (await admin.DeleteAsync($"/api/accounts/{project}", account.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var events = (await admin.GetJsonAsync("/api/scenarios"))[0].GetProperty("events");
        events.GetArrayLength().ShouldBe(1);
        events[0].GetProperty("accountId").GetGuid().ShouldBe(checking);
    }
}
