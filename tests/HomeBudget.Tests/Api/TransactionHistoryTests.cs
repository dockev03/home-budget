using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>B13 : historique des modifications d'une opération.</summary>
public sealed class TransactionHistoryTests
{
    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static JsonElement Field(JsonElement entry, string field) =>
        entry.GetProperty("changes").EnumerateArray().First(c => c.GetProperty("field").GetString() == field);

    [Fact]
    public async Task Each_modification_records_author_date_and_before_after()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant");
        var joint = await admin.CreateAccountAsync("Joint");
        var groceries = await admin.CategoryIdAsync("Courses");
        var restaurants = await admin.CategoryIdAsync("Restaurants");
        var created = await admin.CreateTransactionAsync(checking, -42.5m, groceries, payeeName: "Hyper U", label: "Courses");

        (await admin.GetJsonAsync($"/api/transactions/{Id(created)}/history")).GetArrayLength().ShouldBe(0);

        var updated = await admin.PutAsync($"/api/transactions/{Id(created)}", new
        {
            accountId = joint,
            date = TestScenario.Iso(TestScenario.Today),
            payeeName = "Restaurant du coin",
            label = "Courses",
            lines = new[] { new { categoryId = restaurants, amount = -42.50m, tags = new[] { "Vacances" } } },
        }, created.Version());
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        await admin.CreateAsync($"/api/transactions/{Id(created)}/clear", new { isCleared = true });

        var history = await admin.GetJsonAsync($"/api/transactions/{Id(created)}/history");

        history.GetArrayLength().ShouldBe(2);
        var cleared = history[0];
        cleared.GetProperty("changes").EnumerateArray().ShouldHaveSingleItem().GetProperty("field").GetString().ShouldBe("cleared");
        Field(cleared, "cleared").GetProperty("after").GetString().ShouldBe("true");
        var edit = history[1];
        edit.GetProperty("userName").GetString().ShouldBe(TestData.AdminUserName);
        Field(edit, "account").GetProperty("before").GetString().ShouldBe("Courant");
        Field(edit, "account").GetProperty("after").GetString().ShouldBe("Joint");
        Field(edit, "payee").GetProperty("before").GetString().ShouldBe("Hyper U");
        var lines = Field(edit, "lines");
        lines.GetProperty("beforeLines")[0].GetProperty("category").GetString().ShouldBe("Courses");
        lines.GetProperty("afterLines")[0].GetProperty("category").GetString().ShouldBe("Restaurants");
        lines.GetProperty("afterLines")[0].GetProperty("tags")[0].GetString().ShouldBe("Vacances");
        // Montant identique (42,5 et 42,50) et libellé inchangé : pas de fausse modification.
        edit.GetProperty("changes").EnumerateArray().ShouldNotContain(c => c.GetProperty("field").GetString() == "label");

        // Enregistrer sans rien changer n'ajoute rien.
        var current = await admin.GetJsonAsync($"/api/transactions/{Id(created)}");
        await admin.CreateAsync($"/api/transactions/{Id(created)}/clear", new { isCleared = true });
        (await admin.GetJsonAsync($"/api/transactions/{Id(created)}/history")).GetArrayLength().ShouldBe(2);
        current.Version().ShouldBeGreaterThan(created.Version());
    }

    [Fact]
    public async Task Bulk_edits_are_recorded_for_each_transaction()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var groceries = await admin.CategoryIdAsync("Courses");
        var first = await admin.CreateTransactionAsync(account, -10m, null);
        var second = await admin.CreateTransactionAsync(account, -20m, null);

        await admin.CreateAsync("/api/transactions/bulk", new { ids = new[] { Id(first), Id(second) }, category = new { categoryId = groceries }, addTags = new[] { "Maison" } });

        foreach (var t in new[] { first, second })
        {
            var entry = (await admin.GetJsonAsync($"/api/transactions/{Id(t)}/history")).EnumerateArray().ShouldHaveSingleItem();
            var lines = Field(entry, "lines");
            lines.GetProperty("beforeLines")[0].GetProperty("category").ValueKind.ShouldBe(JsonValueKind.Null);
            lines.GetProperty("afterLines")[0].GetProperty("category").GetString().ShouldBe("Courses");
            lines.GetProperty("afterLines")[0].GetProperty("tags")[0].GetString().ShouldBe("Maison");
        }
    }

    [Fact]
    public async Task History_of_a_private_account_transaction_stays_private()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var mine = await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso Alice",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(TestScenario.Today.AddYears(-1)),
            isPrivate = true,
        });
        var shared = await admin.CreateAccountAsync("Commun");
        var t = await alice.CreateTransactionAsync(Id(mine), -5m, null);
        await alice.CreateAsync($"/api/transactions/{Id(t)}/clear", new { isCleared = true });

        (await admin.GetAsync($"/api/transactions/{Id(t)}/history")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Déplacée vers un compte partagé : l'ancien compte privé n'est pas nommé aux autres membres.
        var current = await alice.GetJsonAsync($"/api/transactions/{Id(t)}");
        (await alice.PutAsync($"/api/transactions/{Id(t)}", new
        {
            accountId = shared,
            date = current.GetProperty("date").GetString(),
            lines = new[] { new { amount = -5m } },
        }, current.Version())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var history = await admin.GetJsonAsync($"/api/transactions/{Id(t)}/history");
        Field(history[0], "account").GetProperty("before").GetString().ShouldBe("Compte privé");
        Field(history[0], "account").GetProperty("after").GetString().ShouldBe("Commun");
    }
}
