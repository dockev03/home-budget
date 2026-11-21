using System.Net;
using System.Text.Json;

using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Tests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

public sealed class BudgetTests
{
    private static JsonElement Line(JsonElement budget, Guid categoryId) =>
        budget.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("categoryId").GetGuid() == categoryId);

    [Fact]
    public async Task Budget_compares_amount_and_percent_modes_with_actual()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");

        (await admin.PutAsync($"/api/budgets/categories/{salary}", new { mode = "Amount", value = 2000m }, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "PercentOfIncome", value = 20m }, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await admin.CreateTransactionAsync(account, 2100m, salary, new DateOnly(2026, 3, 1));
        await admin.CreateTransactionAsync(account, -380m, groceries, new DateOnly(2026, 3, 31));
        await admin.CreateTransactionAsync(account, 20m, groceries, new DateOnly(2026, 3, 15)); // remboursement
        await admin.CreateTransactionAsync(account, -999m, groceries, new DateOnly(2026, 4, 1)); // période suivante
        await admin.CreateTransactionAsync(account, -15m, null, new DateOnly(2026, 3, 10));

        var budget = await admin.GetJsonAsync("/api/budgets/2026-03");

        budget.GetProperty("period").GetProperty("start").GetString().ShouldBe("2026-03-01");
        budget.GetProperty("period").GetProperty("previousKey").GetString().ShouldBe("2026-02");
        budget.Decimal("incomeBase").ShouldBe(2000m);
        budget.Decimal("uncategorized").ShouldBe(-15m);
        var line = Line(budget, groceries);
        line.Decimal("budgeted").ShouldBe(400m);
        line.Decimal("actual").ShouldBe(360m);
        line.GetProperty("status").GetString().ShouldBe("Warning");
        budget.GetProperty("budgeted").Decimal("remaining").ShouldBe(1600m);
        budget.GetProperty("actual").Decimal("remaining").ShouldBe(2100m - 360m);
    }

    [Fact]
    public async Task Transfers_are_excluded_except_savings()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant");
        var livretAccount = await admin.CreateAccountAsync("Livret A", type: "Savings");
        var savings = await admin.CreateCategoryAsync("Livret A", "Savings");
        var date = TestScenario.Iso(new DateOnly(2026, 5, 5));

        await admin.CreateAsync("/api/transfers", new { fromAccountId = checking, toAccountId = livretAccount, date, amount = 200m, categoryId = savings });
        await admin.CreateAsync("/api/transfers", new { fromAccountId = livretAccount, toAccountId = checking, date, amount = 50m });

        var budget = await admin.GetJsonAsync("/api/budgets/2026-05");

        Line(budget, savings).Decimal("actual").ShouldBe(200m);
        budget.GetProperty("actual").Decimal("savings").ShouldBe(200m);
        budget.GetProperty("actual").Decimal("income").ShouldBe(0m);
        budget.Decimal("uncategorized").ShouldBe(0m);
    }

    [Fact]
    public async Task Fixed_expenses_default_to_auto_and_validation_rejects_percent_income()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var internet = await admin.CategoryIdAsync("Internet");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");

        var line = Line(await admin.GetJsonAsync("/api/budgets/2026-03"), internet);
        line.GetProperty("mode").GetString().ShouldBe("Auto");
        line.GetProperty("isDefaultMode").GetBoolean().ShouldBeTrue();
        line.GetProperty("budgetVersion").ValueKind.ShouldBe(JsonValueKind.Null);

        (await admin.PutAsync($"/api/budgets/categories/{salary}", new { mode = "PercentOfIncome", value = 10m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "PercentOfIncome", value = 101m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 10.123m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = -1m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/budgets/categories/{Guid.NewGuid()}", new { mode = "Amount", value = 1m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Updating_and_deleting_a_budget_requires_its_version()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var groceries = await admin.CategoryIdAsync("Courses");
        var url = $"/api/budgets/categories/{groceries}";

        var created = await ApiClient.ReadJsonAsync(await admin.PutAsync(url, new { mode = "Amount", value = 500m }, null));
        created.Version().ShouldBe(1);

        (await admin.PutAsync(url, new { mode = "Amount", value = 550m }, null)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await admin.PutAsync(url, new { mode = "Amount", value = 550m }, 7)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync(url, new { mode = "Auto", value = 550m }, 1));
        updated.Decimal("value").ShouldBe(0m);
        updated.Version().ShouldBe(2);

        (await admin.DeleteAsync(url, 2)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync(url, 3)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Line(await admin.GetJsonAsync("/api/budgets/2026-03"), groceries).GetProperty("mode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Read_only_users_can_read_but_not_set_budgets()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var groceries = await admin.CategoryIdAsync("Courses");
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        (await reader.GetAsync("/api/budgets/2026-03")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 1m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("current")]
    [InlineData("2026-3")]
    public async Task Unknown_period_is_not_found(string key)
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();

        (await admin.GetAsync($"/api/budgets/{key}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fixed_day_period_selects_operations_by_its_own_dates()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var groceries = await admin.CategoryIdAsync("Courses");
        var version = (await admin.GetJsonAsync("/api/settings")).Version();
        (await admin.PutAsync("/api/settings", new { periodMode = "FixedDay", periodStartDay = 25, budgetWarningPercent = 80, budgetAlertPercent = 100 }, version))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await admin.CreateTransactionAsync(account, -10m, groceries, new DateOnly(2026, 10, 24));
        await admin.CreateTransactionAsync(account, -20m, groceries, new DateOnly(2026, 10, 25));
        await admin.CreateTransactionAsync(account, -40m, groceries, new DateOnly(2026, 11, 24));
        await admin.CreateTransactionAsync(account, -80m, groceries, new DateOnly(2026, 11, 25));

        var budget = await admin.GetJsonAsync("/api/budgets/2026-11");

        budget.GetProperty("period").GetProperty("start").GetString().ShouldBe("2026-10-25");
        budget.GetProperty("period").GetProperty("end").GetString().ShouldBe("2026-11-24");
        Line(budget, groceries).Decimal("actual").ShouldBe(60m);
    }

    [Fact]
    public async Task Auto_budgets_come_from_the_schedule_source()
    {
        await using var factory = new ScheduledFactory();
        var admin = await factory.CreateAdminClientAsync();
        var internet = await admin.CategoryIdAsync("Internet");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");
        factory.Schedule.Sums[internet] = -39.99m;
        factory.Schedule.Sums[salary] = 2500m;
        await admin.PutAsync($"/api/budgets/categories/{salary}", new { mode = "Auto", value = 0m }, null);
        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "PercentOfIncome", value = 10m }, null);

        var budget = await admin.GetJsonAsync("/api/budgets/2026-03");

        Line(budget, internet).Decimal("budgeted").ShouldBe(39.99m);
        Line(budget, salary).Decimal("budgeted").ShouldBe(2500m);
        Line(budget, groceries).Decimal("budgeted").ShouldBe(250m);
    }

    private sealed class FakeSchedule : IBudgetScheduleSource
    {
        public Dictionary<Guid, decimal> Sums { get; } = [];

        public Task<IReadOnlyDictionary<Guid, decimal>> GetScheduledSumsAsync(Period period, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(Sums);
    }

    private sealed class ScheduledFactory : HomeBudgetFactory
    {
        public FakeSchedule Schedule { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => s.AddSingleton<IBudgetScheduleSource>(Schedule));
        }
    }
}
