using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class ForecastTests
{
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static DateOnly March(int day) => new(2026, 3, day);

    [Fact]
    public async Task Forecast_combines_balances_schedules_and_budget()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 5000m, type: "Savings", initialDate: Opening);
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");
        var internet = await admin.CategoryIdAsync("Internet");
        var savings = await admin.CreateCategoryAsync("Épargne Livret", "Savings");

        await admin.PutAsync($"/api/budgets/categories/{salary}", new { mode = "Amount", value = 2000m }, null);
        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null);
        await admin.CreateTransactionAsync(checking, 2000m, salary, March(1));
        await admin.CreateTransactionAsync(checking, -100m, groceries, March(2));
        await admin.CreateTransactionAsync(checking, -50m, null, March(15));
        await admin.CreateTransactionAsync(livret, 999m, null, March(3)); // hors prévision

        await admin.CreateAsync("/api/recurrences", new
        {
            label = "Box",
            accountId = checking,
            categoryId = internet,
            amount = -30m,
            mode = "Automatic",
            startDate = TestScenario.Iso(March(20)),
            unit = "Month",
            interval = 1,
            end = "Never",
            isInstallment = false,
        });
        await admin.CreateAsync("/api/recurrences", new
        {
            label = "Épargne",
            accountId = checking,
            toAccountId = livret,
            categoryId = savings,
            amount = 200m,
            mode = "Automatic",
            startDate = TestScenario.Iso(March(25)),
            unit = "Month",
            interval = 1,
            end = "Never",
            isInstallment = false,
        });

        var forecast = await admin.GetJsonAsync("/api/forecast");

        forecast.GetProperty("period").GetProperty("key").GetString().ShouldBe("2026-03");
        forecast.GetProperty("accountIds").EnumerateArray().Select(a => a.GetGuid()).ShouldBe([checking]);
        forecast.Decimal("currentBalance").ShouldBe(2900m);
        forecast.Decimal("futureTransactions").ShouldBe(-50m);
        forecast.Decimal("scheduled").ShouldBe(-230m);
        // Courses : 400 − 100 = 300 restants ; box couverte par son échéance ; salaire déjà reçu.
        forecast.Decimal("budget").ShouldBe(-300m);
        forecast.Decimal("endBalance").ShouldBe(2320m);
        forecast.GetProperty("thresholdDate").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        var daily = forecast.GetProperty("daily").EnumerateArray().ToList();
        daily.Count.ShouldBe(31);
        daily[9].GetProperty("isForecast").GetBoolean().ShouldBeFalse();
        daily[10].GetProperty("isForecast").GetBoolean().ShouldBeTrue();
        daily[^1].Decimal("balance").ShouldBe(2320m);

        var rav = forecast.GetProperty("remainingToLive");
        rav.Decimal("fixed").ShouldBe(80m);
        rav.Decimal("savings").ShouldBe(200m);
        rav.Decimal("total").ShouldBe(2900m - 80m - 200m);
        rav.GetProperty("daysLeft").GetInt32().ShouldBe(22);
        rav.Decimal("perDay").ShouldBe(119.09m);
    }

    [Fact]
    public async Task Threshold_and_income_option_come_from_settings_and_accounts()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 100m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var account = await admin.GetJsonAsync($"/api/accounts/{checking}");
        (await admin.PutAsync($"/api/accounts/{checking}", new
        {
            name = "Courant",
            type = "Checking",
            initialBalance = 100m,
            initialDate = TestScenario.Iso(Opening),
            overdraftThreshold = 50m,
        }, account.Version())).EnsureSuccessStatusCode();
        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 210m }, null);

        var forecast = await admin.GetJsonAsync("/api/forecast");

        forecast.Decimal("threshold").ShouldBe(50m);
        // 210 € étalés sur 21 jours : 10 € par jour ; sous 50 € le 16 mars (100 − 6 × 10 = 40).
        forecast.GetProperty("thresholdDate").GetString().ShouldBe("2026-03-16");
        forecast.GetProperty("includesIncome").GetBoolean().ShouldBeTrue();
    }
}
