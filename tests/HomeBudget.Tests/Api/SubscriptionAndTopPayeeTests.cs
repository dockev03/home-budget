using System.Text.Json;

using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class SubscriptionAndTopPayeeTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static object Recurrence(
        Guid accountId, string label, decimal amount, string unit, bool isSubscription, Guid? toAccountId = null,
        string end = "Never", int? count = null, DateOnly? start = null) => new
        {
            label,
            accountId,
            toAccountId,
            payeeName = toAccountId is null ? label : null,
            amount,
            mode = "Automatic",
            startDate = TestScenario.Iso(start ?? Today.AddDays(3)),
            unit,
            interval = 1,
            end,
            count,
            isInstallment = false,
            isSubscription,
        };

    [Theory]
    [InlineData(9.99, FrequencyUnit.Month, 1, 119.88, 9.99)]
    [InlineData(60, FrequencyUnit.Year, 1, 60, 5)]
    [InlineData(5, FrequencyUnit.Week, 2, 130, 10.83)]
    [InlineData(10, FrequencyUnit.Month, 3, 40, 3.33)]
    [InlineData(1, FrequencyUnit.Day, 1, 365, 30.42)]
    public void Subscription_cost_is_brought_back_to_the_year_and_the_month(
        double amount, FrequencyUnit unit, int interval, double annual, double monthly)
    {
        SubscriptionCost.Annual((decimal)amount, unit, interval).ShouldBe((decimal)annual);
        SubscriptionCost.Monthly((decimal)amount, unit, interval).ShouldBe((decimal)monthly);
    }

    [Fact]
    public async Task Subscriptions_view_totals_the_active_subscriptions_only()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2026, 1, 1));
        var livret = await admin.CreateAccountAsync("Livret A", 0m, "Savings", new DateOnly(2026, 1, 1));
        await admin.CreateAsync("/api/recurrences", Recurrence(account, "Netflix", -13.49m, "Month", true));
        await admin.CreateAsync("/api/recurrences", Recurrence(account, "Antivirus", -36m, "Year", true));
        await admin.CreateAsync("/api/recurrences", Recurrence(account, "Loyer", -700m, "Month", false));
        // Terminé : sa dernière échéance est passée.
        await admin.CreateAsync("/api/recurrences", Recurrence(
            account, "Ancien abonnement", -5m, "Month", true, end: "AfterCount", count: 1, start: new DateOnly(2026, 2, 1)));
        // Un virement n'est jamais un abonnement.
        var transfer = await admin.CreateAsync("/api/recurrences", Recurrence(account, "Épargne", 50m, "Month", true, livret));
        transfer.GetProperty("isSubscription").GetBoolean().ShouldBeFalse();

        var view = await admin.GetJsonAsync("/api/recurrences/subscriptions");

        var items = view.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("label").GetString()).ShouldBe(["Netflix", "Antivirus"]);
        items[0].Decimal("monthlyCost").ShouldBe(13.49m);
        items[1].Decimal("annualCost").ShouldBe(36m);
        view.Decimal("monthlyTotal").ShouldBe(16.49m);
        view.Decimal("annualTotal").ShouldBe(197.88m);
    }

    [Fact]
    public async Task Dashboard_ranks_payees_by_expenses_of_the_period()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 1000m, initialDate: new DateOnly(2026, 1, 1));
        var groceries = await admin.CategoryIdAsync("Courses");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var day = new DateOnly(2026, 11, 10);
        await admin.CreateTransactionAsync(account, -80m, groceries, day, payeeName: "Leclerc");
        await admin.CreateTransactionAsync(account, -40m, groceries, day, payeeName: "Leclerc");
        await admin.CreateTransactionAsync(account, 10m, groceries, day, payeeName: "Leclerc"); // remboursement déduit
        await admin.CreateTransactionAsync(account, -60m, groceries, day, payeeName: "Boulangerie");
        await admin.CreateTransactionAsync(account, 2000m, salary, day, payeeName: "Employeur"); // revenu : exclu
        await admin.CreateTransactionAsync(account, -500m, groceries, new DateOnly(2026, 10, 10), payeeName: "Octobre"); // hors période
        await admin.CreateTransactionAsync(account, -15m, null, day, payeeName: "Sans catégorie"); // ni dépense ni revenu

        var top = (await admin.GetJsonAsync("/api/dashboard/2026-11")).GetProperty("topPayees").EnumerateArray().ToList();

        top.Select(p => p.GetProperty("name").GetString()).ShouldBe(["Leclerc", "Boulangerie"]);
        top[0].Decimal("amount").ShouldBe(110m);
        top[0].GetProperty("count").GetInt32().ShouldBe(3);
    }
}
