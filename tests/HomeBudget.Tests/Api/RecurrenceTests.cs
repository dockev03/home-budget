using System.Net;
using System.Text.Json;

using HomeBudget.Api.Features.Recurrences;
using HomeBudget.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

public sealed class RecurrenceTests
{
    /// <summary>Date du jour figée pour ces tests.</summary>
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static HomeBudgetFactory Factory() => new() { Today = Today };

    private static object Recurrence(
        Guid accountId, decimal amount, DateOnly start, string label = "Box", Guid? categoryId = null, string mode = "Automatic",
        string unit = "Month", int interval = 1, string end = "Never", int? count = null, bool isInstallment = false,
        Guid? toAccountId = null, string? payeeName = null) => new
        {
            label,
            accountId,
            toAccountId,
            payeeName,
            categoryId,
            amount,
            notes = (string?)null,
            mode,
            startDate = TestScenario.Iso(start),
            unit,
            interval,
            end,
            endDate = (string?)null,
            count,
            isInstallment,
        };

    private static async Task<List<JsonElement>> TransactionsAsync(ApiClient client, Guid accountId) =>
        [.. (await client.GetJsonAsync($"/api/transactions?accountId={accountId}")).GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Automatic_recurrence_generates_today_once_and_never_the_past()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var internet = await admin.CategoryIdAsync("Internet");

        // Commencée le 10 février : l'échéance de février, antérieure à la création, n'est pas recréée.
        var created = await admin.CreateAsync("/api/recurrences",
            Recurrence(account, -30m, new DateOnly(2026, 2, 10), categoryId: internet, payeeName: "Free"));

        created.GetProperty("nextDate").GetString().ShouldBe("2026-04-10");
        var transactions = await TransactionsAsync(admin, account);
        var generated = transactions.ShouldHaveSingleItem();
        generated.GetProperty("date").GetString().ShouldBe("2026-03-10");
        generated.Decimal("amount").ShouldBe(-30m);
        generated.GetProperty("payeeName").GetString().ShouldBe("Free");
        generated.GetProperty("isCleared").GetBoolean().ShouldBeFalse();

        // L'opération supprimée n'est pas régénérée (R6), même après une modification ou un nouveau passage.
        (await admin.DeleteAsync($"/api/transactions/{generated.GetProperty("id").GetGuid()}", generated.Version())).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        var id = created.GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/recurrences/{id}",
            Recurrence(account, -31m, new DateOnly(2026, 2, 10), label: "Box internet", categoryId: internet), created.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<RecurrenceGenerator>().RunAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        }
        (await TransactionsAsync(admin, account)).ShouldBeEmpty();

        var list = await admin.GetJsonAsync("/api/recurrences");
        list.EnumerateArray().ShouldHaveSingleItem().GetProperty("label").GetString().ShouldBe("Box internet");
    }

    [Fact]
    public async Task Pending_occurrences_can_be_confirmed_skipped_or_postponed()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);

        await admin.CreateAsync("/api/recurrences", Recurrence(account, -40m, Today, label: "Électricité", mode: "ToValidate"));
        await admin.CreateAsync("/api/recurrences", Recurrence(account, -15m, Today, label: "Cantine", mode: "ToValidate", unit: "Week"));
        await admin.CreateAsync("/api/recurrences", Recurrence(account, 120m, Today, label: "CAF", mode: "ToValidate"));
        (await TransactionsAsync(admin, account)).ShouldBeEmpty();

        var pending = (await admin.GetJsonAsync("/api/recurrences/pending")).EnumerateArray().ToList();
        pending.Select(p => p.GetProperty("label").GetString()).ShouldBe(["CAF", "Cantine", "Électricité"]);
        Guid Pending(string label) => pending.First(p => p.GetProperty("label").GetString() == label).GetProperty("id").GetGuid();

        // Confirmation avec un montant ajusté.
        var confirm = await admin.PostAsync($"/api/recurrences/occurrences/{Pending("Électricité")}/confirm", new { amount = -43.5m });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiClient.ReadJsonAsync(confirm)).GetProperty("status").GetString().ShouldBe("Generated");
        var transaction = (await TransactionsAsync(admin, account)).ShouldHaveSingleItem();
        transaction.Decimal("amount").ShouldBe(-43.5m);
        transaction.GetProperty("label").GetString().ShouldBe("Électricité");
        (await admin.PostAsync($"/api/recurrences/occurrences/{Pending("Électricité")}/confirm", new { })).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);

        (await admin.PostAsync($"/api/recurrences/occurrences/{Pending("Cantine")}/skip")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/recurrences/occurrences/{Pending("CAF")}/postpone", new { date = "2026-03-09" })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync($"/api/recurrences/occurrences/{Pending("CAF")}/postpone", new { date = "2026-03-20" })).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        (await admin.GetJsonAsync("/api/recurrences/pending")).GetArrayLength().ShouldBe(0);
        (await TransactionsAsync(admin, account)).Count.ShouldBe(1);
        (await admin.PostAsync($"/api/recurrences/occurrences/{Guid.NewGuid()}/skip")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var caf = (await admin.GetJsonAsync("/api/recurrences")).EnumerateArray().First(r => r.GetProperty("label").GetString() == "CAF");
        caf.GetProperty("nextDate").GetString().ShouldBe("2026-03-20");
        caf.GetProperty("pendingCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Installments_are_numbered_and_transfers_create_two_operations()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", type: "Savings", initialDate: Opening);
        var savings = await admin.CreateCategoryAsync("Épargne Livret", "Savings");

        // Paiement en 4 fois commencé en janvier : l'échéance de mars est la 3e.
        var watch = await admin.CreateAsync("/api/recurrences",
            Recurrence(checking, -100m, new DateOnly(2026, 1, 10), label: "Montre", end: "AfterCount", count: 4, isInstallment: true));
        watch.GetProperty("nextDate").GetString().ShouldBe("2026-04-10");

        await admin.CreateAsync("/api/recurrences",
            Recurrence(checking, 200m, Today, label: "Épargne", categoryId: savings, toAccountId: livret));

        var operations = await TransactionsAsync(admin, checking);
        operations.Select(o => o.GetProperty("label").GetString()).ShouldBe(["Montre 3/4", "Épargne"], ignoreOrder: true);
        var transfer = operations.First(o => o.GetProperty("label").GetString() == "Épargne");
        transfer.Decimal("amount").ShouldBe(-200m);
        transfer.GetProperty("transferAccountId").GetGuid().ShouldBe(livret);
        (await TransactionsAsync(admin, livret)).ShouldHaveSingleItem().Decimal("amount").ShouldBe(200m);

        var budget = await admin.GetJsonAsync("/api/budgets/2026-03");
        budget.GetProperty("categories").EnumerateArray()
            .First(c => c.GetProperty("categoryId").GetGuid() == savings).Decimal("actual").ShouldBe(200m);
    }

    [Fact]
    public async Task Auto_budget_sums_the_planned_occurrences_of_the_period()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var internet = await admin.CategoryIdAsync("Internet");

        await admin.CreateAsync("/api/recurrences", Recurrence(account, -30m, new DateOnly(2026, 3, 20), categoryId: internet));
        await admin.CreateAsync("/api/recurrences",
            Recurrence(account, -5m, new DateOnly(2026, 3, 2), label: "Option", categoryId: internet, unit: "Week"));

        static decimal Budgeted(JsonElement budget, Guid id) =>
            budget.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("categoryId").GetGuid() == id).Decimal("budgeted");

        // Mars : 30 € + 5 lundis (2, 9, 16, 23, 30) × 5 € ; février : rien.
        Budgeted(await admin.GetJsonAsync("/api/budgets/2026-03"), internet).ShouldBe(55m);
        Budgeted(await admin.GetJsonAsync("/api/budgets/2026-02"), internet).ShouldBe(0m);
    }

    [Fact]
    public async Task Invalid_recurrences_are_rejected_and_deletion_keeps_operations()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");

        async Task<IEnumerable<string>> ErrorsAsync(object body)
        {
            var response = await admin.PostAsync("/api/recurrences", body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await ApiClient.ReadJsonAsync(response)).GetProperty("errors").EnumerateObject().Select(e => e.Name);
        }

        (await ErrorsAsync(Recurrence(account, -10m, Today, isInstallment: true))).ShouldContain("end");
        (await ErrorsAsync(Recurrence(account, -10m, Today, interval: 0))).ShouldContain("interval");
        (await ErrorsAsync(Recurrence(account, 0m, Today))).ShouldContain("amount");
        (await ErrorsAsync(Recurrence(account, -10m, Today, toAccountId: account))).ShouldContain("toAccountId");
        (await ErrorsAsync(Recurrence(account, 10m, Today, toAccountId: Guid.NewGuid(), categoryId: groceries)))
            .ShouldBe(["toAccountId", "categoryId"], ignoreOrder: true);

        var created = await admin.CreateAsync("/api/recurrences", Recurrence(account, -10m, Today));
        var id = created.GetProperty("id").GetGuid();
        (await admin.DeleteAsync($"/api/accounts/{account}", 1)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/recurrences/{id}", null)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await admin.DeleteAsync($"/api/recurrences/{id}", created.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/recurrences/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionsAsync(admin, account)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Readers_cannot_write_recurrences()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var reader = await factory.CreateUserClientAsync("lecteur", HomeBudget.Api.Domain.Users.Roles.ReadOnly);

        (await reader.PostAsync("/api/recurrences", Recurrence(account, -10m, Today))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/api/recurrences")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
