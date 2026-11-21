using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class TransferTests
{
    [Fact]
    public async Task Transfer_moves_money_between_two_accounts()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var courant = await admin.CreateAccountAsync("Courant", 1000m);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, "Savings");

        var transfer = await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = courant,
            toAccountId = livret,
            date = TestScenario.Iso(TestScenario.Today),
            amount = 300m,
            label = "Épargne du mois",
        });

        (await admin.GetJsonAsync($"/api/accounts/{courant}")).Decimal("balance").ShouldBe(700m);
        (await admin.GetJsonAsync($"/api/accounts/{livret}")).Decimal("balance").ShouldBe(300m);
        var legs = (await admin.GetJsonAsync("/api/transactions")).GetProperty("items").EnumerateArray().ToList();
        legs.Count.ShouldBe(2);
        legs.ShouldAllBe(t => t.GetProperty("transferId").GetGuid() == transfer.GetProperty("transferId").GetGuid());
        legs.Single(t => t.GetProperty("accountId").GetGuid() == courant).GetProperty("transferAccountId").GetGuid().ShouldBe(livret);
    }

    [Fact]
    public async Task Transfer_legs_are_edited_through_the_transfer()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var courant = await admin.CreateAccountAsync("Courant", 1000m);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, "Savings");
        var epargne = await admin.CreateCategoryAsync("Épargne", "Savings");
        var transfer = await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = courant,
            toAccountId = livret,
            date = TestScenario.Iso(TestScenario.Today),
            amount = 300m,
        });
        var transferId = transfer.GetProperty("transferId").GetGuid();
        var leg = transfer.GetProperty("fromTransactionId").GetGuid();

        (await admin.PutAsync($"/api/transactions/{leg}", new
        {
            accountId = courant,
            date = TestScenario.Iso(TestScenario.Today),
            lines = new[] { new { amount = -1m } },
        }, 1)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/transactions/{leg}", 1)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var updated = await admin.PutAsync($"/api/transfers/{transferId}", new
        {
            fromAccountId = courant,
            toAccountId = livret,
            date = TestScenario.Iso(TestScenario.Today),
            amount = 450m,
            categoryId = epargne,
        }, transfer.Version());
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await ApiClient.ReadJsonAsync(updated);
        json.Decimal("amount").ShouldBe(450m);
        json.GetProperty("categoryId").GetGuid().ShouldBe(epargne);
        (await admin.GetJsonAsync($"/api/accounts/{livret}")).Decimal("balance").ShouldBe(450m);

        (await admin.DeleteAsync($"/api/transfers/{transferId}", json.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync("/api/transactions")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_transfers_are_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var courant = await admin.CreateAccountAsync("Courant");
        var livret = await admin.CreateAccountAsync("Livret A", 0m, "Savings");
        var courses = await admin.CategoryIdAsync("Courses");
        var date = TestScenario.Iso(TestScenario.Today);

        (await admin.PostAsync("/api/transfers", new { fromAccountId = courant, toAccountId = courant, date, amount = 10m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/transfers", new { fromAccountId = courant, toAccountId = livret, date, amount = -10m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/transfers", new { fromAccountId = courant, toAccountId = livret, date, amount = 10m, categoryId = courses }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
