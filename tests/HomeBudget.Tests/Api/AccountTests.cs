using System.Net;

using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

public sealed class AccountTests
{
    [Fact]
    public async Task Balance_counts_past_transactions_and_future_ones_separately()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Compte joint", 1000m);
        await admin.CreateTransactionAsync(account, -120.45m, date: TestScenario.Today.AddDays(-3));
        await admin.CreateTransactionAsync(account, 2500m, date: TestScenario.Today);
        await admin.CreateTransactionAsync(account, -800m, date: TestScenario.Today.AddDays(10));

        var json = await admin.GetJsonAsync($"/api/accounts/{account}");

        json.Decimal("balance").ShouldBe(3379.55m);
        json.Decimal("futureBalance").ShouldBe(2579.55m);
    }

    [Fact]
    public async Task Net_worth_sums_active_accounts_included_in_net_worth()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Courant", 500m);
        await admin.CreateAccountAsync("Livret A", 10_000m, "Savings");
        var loan = await admin.CreateAccountAsync("Crédit", -2_000m, "Credit");
        var archived = await admin.CreateAccountAsync("Ancien", 50m);
        var archivedJson = await admin.GetJsonAsync($"/api/accounts/{archived}");
        (await admin.PutAsync($"/api/accounts/{archived}", new
        {
            name = "Ancien",
            type = "Checking",
            initialBalance = 50m,
            initialDate = archivedJson.GetProperty("initialDate").GetString(),
            isArchived = true,
        }, archivedJson.Version())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await admin.GetJsonAsync("/api/accounts");

        list.Decimal("netWorth").ShouldBe(8_500m);
        list.GetProperty("items").GetArrayLength().ShouldBe(3);
        (await admin.GetJsonAsync("/api/accounts?includeArchived=true")).GetProperty("items").GetArrayLength().ShouldBe(4);
        var loanJson = await admin.GetJsonAsync($"/api/accounts/{loan}");
        loanJson.GetProperty("includeInForecast").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Duplicate_name_is_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Courant");

        var response = await admin.PostAsync("/api/accounts", new { name = "courant", type = "Checking", initialBalance = 0, initialDate = "2026-01-01" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_requires_current_version()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var id = await admin.CreateAccountAsync("Courant");
        var body = new { name = "Compte courant", type = "Checking", initialBalance = 0, initialDate = "2026-01-01" };

        (await admin.PutAsync($"/api/accounts/{id}", body, null)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        var updated = await admin.PutAsync($"/api/accounts/{id}", body, 1);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiClient.ReadJsonAsync(updated)).Version().ShouldBe(2);
        (await admin.PutAsync($"/api/accounts/{id}", body, 1)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Account_with_transactions_cannot_be_deleted()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var used = await admin.CreateAccountAsync("Utilisé");
        var empty = await admin.CreateAccountAsync("Vide");
        await admin.CreateTransactionAsync(used, -10m);

        (await admin.DeleteAsync($"/api/accounts/{used}", 1)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/accounts/{empty}", 1)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Creation_is_traced()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var id = await admin.CreateAccountAsync("Courant");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.FindAsync([id], TestContext.Current.CancellationToken);

        account.ShouldNotBeNull();
        account.CreatedBy.ShouldBe(me.GetProperty("id").GetGuid());
        account.Version.ShouldBe(1);
    }
}
