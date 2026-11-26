using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class ReconciliationTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static HomeBudgetFactory Factory() => new() { Today = Today };

    private static string Iso(DateOnly date) => TestScenario.Iso(date);

    private static async Task<JsonElement> ClearAsync(ApiClient client, JsonElement transaction, bool isCleared = true) =>
        await client.CreateAsync($"/api/transactions/{transaction.GetProperty("id").GetGuid()}/clear", new { isCleared });

    private static async Task<JsonElement> PreviewAsync(ApiClient client, Guid account, DateOnly date, decimal balance) =>
        await client.GetJsonAsync(
            $"/api/accounts/{account}/reconciliations/preview?date={Iso(date)}&statementBalance={balance.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    [Fact]
    public async Task Reconciliation_compares_the_statement_with_cleared_transactions_and_locks_them()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var rent = await ClearAsync(admin, await admin.CreateTransactionAsync(account, -700m, null, new DateOnly(2026, 11, 5)));
        var shop = await admin.CreateTransactionAsync(account, -50m, groceries, new DateOnly(2026, 11, 10));
        var later = await ClearAsync(admin, await admin.CreateTransactionAsync(account, -20m, groceries, new DateOnly(2026, 11, 25)));

        // Le relevé au 20/11 indique 250 € : la course n'est pas encore pointée.
        var preview = await PreviewAsync(admin, account, new DateOnly(2026, 11, 20), 250m);
        preview.Decimal("clearedBalance").ShouldBe(300m);
        preview.Decimal("difference").ShouldBe(-50m);
        preview.GetProperty("unclearedCount").GetInt32().ShouldBe(1);
        var refused = await admin.PostAsync($"/api/accounts/{account}/reconciliations", new { date = "2026-11-20", statementBalance = 250m });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        shop = await ClearAsync(admin, shop);
        (await PreviewAsync(admin, account, new DateOnly(2026, 11, 20), 250m)).Decimal("difference").ShouldBe(0m);
        var reconciliation = await admin.CreateAsync($"/api/accounts/{account}/reconciliations", new { date = "2026-11-20", statementBalance = 250m });
        reconciliation.GetProperty("transactionCount").GetInt32().ShouldBe(2);

        // Opérations rapprochées : verrouillées. L'opération postérieure au relevé ne l'est pas.
        var locked = await admin.GetJsonAsync($"/api/transactions/{shop.GetProperty("id").GetGuid()}");
        locked.GetProperty("isReconciled").GetBoolean().ShouldBeTrue();
        (await admin.GetJsonAsync($"/api/transactions/{later.GetProperty("id").GetGuid()}")).GetProperty("isReconciled").GetBoolean().ShouldBeFalse();
        var update = await admin.PutAsync($"/api/transactions/{locked.GetProperty("id").GetGuid()}", new
        {
            accountId = account,
            date = "2026-11-10",
            lines = new[] { new { categoryId = groceries, amount = -60m } },
        }, locked.Version());
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/transactions/{rent.GetProperty("id").GetGuid()}", rent.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/transactions/{rent.GetProperty("id").GetGuid()}/clear", new { isCleared = false }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var bulk = await admin.CreateAsync("/api/transactions/bulk", new { ids = new[] { shop.GetProperty("id").GetGuid() }, addTags = new[] { "x" } });
        bulk.GetProperty("skipped").GetInt32().ShouldBe(1);

        // Un rapprochement antérieur ou à la même date est refusé.
        var earlier = await admin.PostAsync($"/api/accounts/{account}/reconciliations", new { date = "2026-11-15", statementBalance = 300m });
        earlier.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_the_last_reconciliation_can_be_cancelled_and_it_unlocks_its_transactions()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 100m, initialDate: Opening);
        var first = await ClearAsync(admin, await admin.CreateTransactionAsync(account, -10m, null, new DateOnly(2026, 10, 5)));
        await ClearAsync(admin, await admin.CreateTransactionAsync(account, -20m, null, new DateOnly(2026, 11, 5)));
        var october = await admin.CreateAsync($"/api/accounts/{account}/reconciliations", new { date = "2026-10-31", statementBalance = 90m });
        var november = await admin.CreateAsync($"/api/accounts/{account}/reconciliations", new { date = "2026-11-25", statementBalance = 70m });
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        var list = (await admin.GetJsonAsync($"/api/accounts/{account}/reconciliations")).EnumerateArray().ToList();
        list.Select(r => r.GetProperty("date").GetString()).ShouldBe(["2026-11-25", "2026-10-31"]);

        (await admin.DeleteAsync($"/api/accounts/{account}/reconciliations/{october.GetProperty("id").GetGuid()}", october.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reader.DeleteAsync($"/api/accounts/{account}/reconciliations/{november.GetProperty("id").GetGuid()}", november.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/accounts/{account}/reconciliations/{november.GetProperty("id").GetGuid()}", november.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/accounts/{account}/reconciliations/{october.GetProperty("id").GetGuid()}", october.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var unlocked = await admin.GetJsonAsync($"/api/transactions/{first.GetProperty("id").GetGuid()}");
        unlocked.GetProperty("isReconciled").GetBoolean().ShouldBeFalse();
        unlocked.GetProperty("isCleared").GetBoolean().ShouldBeTrue();
        (await admin.DeleteAsync($"/api/transactions/{first.GetProperty("id").GetGuid()}", unlocked.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_transfer_with_a_reconciled_leg_is_locked()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 500m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, "Savings", Opening);
        var transfer = await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = checking,
            toAccountId = livret,
            date = "2026-11-02",
            amount = 100m,
        });
        var leg = await admin.GetJsonAsync($"/api/transactions/{transfer.GetProperty("fromTransactionId").GetGuid()}");
        await ClearAsync(admin, leg);
        await admin.CreateAsync($"/api/accounts/{checking}/reconciliations", new { date = "2026-11-03", statementBalance = 400m });

        var transferId = transfer.GetProperty("transferId").GetGuid();
        (await admin.DeleteAsync($"/api/transfers/{transferId}", transfer.Version())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutAsync($"/api/transfers/{transferId}", new
        {
            fromAccountId = checking,
            toAccountId = livret,
            date = "2026-11-02",
            amount = 150m,
        }, transfer.Version())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Future_or_pre_opening_dates_are_rejected()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 0m, initialDate: Opening);

        (await admin.GetAsync($"/api/accounts/{account}/reconciliations/preview?date=2026-12-01&statementBalance=0"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/accounts/{account}/reconciliations/preview?date=2025-12-31&statementBalance=0"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/accounts/{Guid.NewGuid()}/reconciliations/preview?date=2026-11-01&statementBalance=0"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
