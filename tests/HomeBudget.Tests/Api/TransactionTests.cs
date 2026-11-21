using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class TransactionTests
{
    [Fact]
    public async Task Split_transaction_amount_is_the_sum_of_its_lines()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var courses = await admin.CategoryIdAsync("Courses");
        var chien = await admin.CategoryIdAsync("Chien");

        var json = await admin.CreateAsync("/api/transactions", new
        {
            accountId = account,
            date = TestScenario.Iso(TestScenario.Today),
            payeeName = "Leclerc",
            notes = "Croquettes comprises",
            lines = new object[]
            {
                new { categoryId = courses, amount = -85.30m, memo = "Alimentation" },
                new { categoryId = chien, amount = -24.99m, memo = "Croquettes" },
            },
        });

        json.Decimal("amount").ShouldBe(-110.29m);
        json.GetProperty("lines").GetArrayLength().ShouldBe(2);
        json.GetProperty("payeeName").GetString().ShouldBe("Leclerc");
        json.GetProperty("notes").GetString().ShouldBe("Croquettes comprises");
    }

    [Fact]
    public async Task Payee_is_reused_regardless_of_case()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");

        var first = await admin.CreateTransactionAsync(account, -10m, payeeName: "Boulangerie Paul");
        var second = await admin.CreateTransactionAsync(account, -12m, payeeName: "  boulangerie   PAUL ");

        second.GetProperty("payeeId").GetGuid().ShouldBe(first.GetProperty("payeeId").GetGuid());
        var payees = await admin.GetJsonAsync("/api/payees?search=boul");
        payees.GetArrayLength().ShouldBe(1);
        payees[0].GetProperty("usageCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Update_replaces_lines()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var courses = await admin.CategoryIdAsync("Courses");
        var created = await admin.CreateTransactionAsync(account, -50m, courses);
        var id = created.GetProperty("id").GetGuid();

        var response = await admin.PutAsync($"/api/transactions/{id}", new
        {
            accountId = account,
            date = TestScenario.Iso(TestScenario.Today),
            lines = new object[] { new { categoryId = courses, amount = -30m }, new { categoryId = (Guid?)null, amount = -25m } },
        }, created.Version());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await ApiClient.ReadJsonAsync(response);
        json.Decimal("amount").ShouldBe(-55m);
        json.GetProperty("lines").GetArrayLength().ShouldBe(2);
        json.Version().ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_transactions_are_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2026, 1, 1));

        async Task<HttpStatusCode> Post(object body) => (await admin.PostAsync("/api/transactions", body)).StatusCode;

        (await Post(new { accountId = account, date = "2025-12-31", lines = new[] { new { amount = -1m } } })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new { accountId = account, date = "2026-02-01", lines = new[] { new { amount = 0m } } })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new { accountId = account, date = "2026-02-01", lines = new[] { new { amount = -1.234m } } })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new { accountId = account, date = "2026-02-01", lines = Array.Empty<object>() })).ShouldBe(HttpStatusCode.BadRequest);
        (await Post(new { accountId = Guid.NewGuid(), date = "2026-02-01", lines = new[] { new { amount = -1m } } })).ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Clearing_toggles_the_flag()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var id = (await admin.CreateTransactionAsync(account, -10m)).GetProperty("id").GetGuid();

        var json = await admin.CreateAsync($"/api/transactions/{id}/clear", new { isCleared = true });

        json.GetProperty("isCleared").GetBoolean().ShouldBeTrue();
        (await admin.GetJsonAsync("/api/transactions?cleared=false")).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await admin.GetJsonAsync("/api/transactions?cleared=true")).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Search_filters_combine()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var courant = await admin.CreateAccountAsync("Courant");
        var autre = await admin.CreateAccountAsync("Autre");
        var courses = await admin.CategoryIdAsync("Courses");
        var drive = await admin.CreateCategoryAsync("Drive", "Variable", courses);
        var salaire = await admin.CategoryIdAsync("Salaire Kévin");
        var d = TestScenario.Today.AddDays(-20);

        await admin.CreateTransactionAsync(courant, -30m, courses, d, "Lidl");
        await admin.CreateTransactionAsync(courant, -120m, drive, d.AddDays(1), "Leclerc Drive", "Commande 100%");
        await admin.CreateTransactionAsync(courant, 2500m, salaire, d.AddDays(2), "Employeur");
        await admin.CreateTransactionAsync(autre, -60m, courses, d.AddDays(3), "Lidl");

        async Task<int> Count(string query) =>
            (await admin.GetJsonAsync($"/api/transactions?{query}")).GetProperty("items").GetArrayLength();

        (await Count($"accountId={courant}")).ShouldBe(3);
        (await Count($"categoryId={courses}")).ShouldBe(3); // inclut la sous-catégorie Drive
        (await Count($"categoryId={drive}")).ShouldBe(1);
        (await Count("minAmount=50")).ShouldBe(3); // valeur absolue : -120, 2500, -60
        (await Count("minAmount=50&maxAmount=200")).ShouldBe(2);
        (await Count("search=lidl")).ShouldBe(2); // nom du tiers
        (await Count("search=100%25")).ShouldBe(1); // « % » est cherché littéralement
        (await Count($"from={TestScenario.Iso(d.AddDays(1))}&to={TestScenario.Iso(d.AddDays(2))}")).ShouldBe(2);
        var payeeId = (await admin.GetJsonAsync("/api/payees?search=lidl"))[0].GetProperty("id").GetGuid();
        (await Count($"payeeId={payeeId}&accountId={autre}")).ShouldBe(1);
    }

    [Fact]
    public async Task Cursor_pagination_returns_every_transaction_once_newest_first()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        for (var i = 0; i < 7; i++)
        {
            // Plusieurs opérations le même jour : le curseur doit les départager.
            await admin.CreateTransactionAsync(account, -(i + 1), date: TestScenario.Today.AddDays(-(i / 3)));
        }

        var seen = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var url = "/api/transactions?limit=3" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await admin.GetJsonAsync(url);
            seen.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        seen.Count.ShouldBe(7);
        seen.Select(t => t.GetProperty("id").GetGuid()).Distinct().Count().ShouldBe(7);
        var dates = seen.Select(t => t.GetProperty("date").GetString()).ToList();
        dates.ShouldBe(dates.OrderByDescending(x => x, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Read_only_user_cannot_write()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Courant");
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        (await reader.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsync("/api/accounts", new { name = "X", type = "Checking", initialBalance = 0, initialDate = "2026-01-01" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
