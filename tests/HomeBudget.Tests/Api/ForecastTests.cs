using System.Net;
using System.Text.Json;

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

    private static DateOnly April(int day) => new(2026, 4, day);

    [Fact]
    public async Task Forecast_spans_three_six_or_twelve_periods()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var internet = await admin.CategoryIdAsync("Internet");
        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null);
        await admin.CreateTransactionAsync(checking, -50m, groceries, April(15));
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

        var forecast = await admin.GetJsonAsync("/api/forecast?horizon=3");

        forecast.GetProperty("horizon").GetInt32().ShouldBe(3);
        forecast.GetProperty("strategy").GetString().ShouldBe("Budget");
        // Mars : − 30 de box − 400 de courses ; avril : − 30 − 50 saisis − 350 restants ; mai : − 30 − 400.
        forecast.Decimal("endBalance").ShouldBe(570m);
        var periods = forecast.GetProperty("periods").EnumerateArray().ToList();
        periods.Select(p => p.GetProperty("key").GetString()).ShouldBe(["2026-03", "2026-04", "2026-05"]);
        periods.Select(p => p.Decimal("endBalance")).ShouldBe([570m, 140m, -290m]);
        periods.ShouldAllBe(p => p.GetProperty("scenarioEndBalance").ValueKind == JsonValueKind.Null);
        var daily = forecast.GetProperty("daily").EnumerateArray().ToList();
        daily.Count.ShouldBe(31 + 30 + 31);
        daily[^1].Decimal("balance").ShouldBe(-290m);
        forecast.GetProperty("thresholdDate").GetString()!.ShouldStartWith("2026-05-");
        forecast.GetProperty("scenario").ValueKind.ShouldBe(JsonValueKind.Null);

        // Par défaut, la période en cours seule ; la prévision de mars ne bouge pas.
        var current = await admin.GetJsonAsync("/api/forecast");
        current.GetProperty("periods").GetArrayLength().ShouldBe(1);
        current.Decimal("endBalance").ShouldBe(570m);
        current.GetProperty("thresholdDate").ValueKind.ShouldBe(JsonValueKind.Null);

        (await admin.GetJsonAsync("/api/forecast?horizon=12")).GetProperty("periods").GetArrayLength().ShouldBe(12);
        (await admin.GetAsync("/api/forecast?horizon=2")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Average_strategy_replaces_the_budget_by_the_mean_of_closed_periods()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 5000m, initialDate: Opening);
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");
        // Un budget défini est ignoré par la stratégie de la moyenne.
        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 1000m }, null);
        await admin.CreateTransactionAsync(checking, 2000m, salary, new DateOnly(2026, 1, 10));
        await admin.CreateTransactionAsync(checking, -300m, groceries, new DateOnly(2026, 1, 12));
        await admin.CreateTransactionAsync(checking, 2000m, salary, new DateOnly(2026, 2, 10));
        await admin.CreateTransactionAsync(checking, -500m, groceries, new DateOnly(2026, 2, 12));
        await admin.CreateTransactionAsync(checking, -100m, groceries, March(2));
        var settings = await admin.GetJsonAsync("/api/settings");
        (await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 80,
            budgetAlertPercent = 100,
            forecastStrategy = "Average",
            forecastAverageMonths = 2,
        }, settings.Version())).EnsureSuccessStatusCode();

        var forecast = await admin.GetJsonAsync("/api/forecast?horizon=3");

        forecast.GetProperty("strategy").GetString().ShouldBe("Average");
        forecast.GetProperty("averageMonths").GetInt32().ShouldBe(2);
        forecast.Decimal("currentBalance").ShouldBe(8100m);
        // Moyennes : courses 400, salaire 2 000. Mars : 300 de courses restants et le salaire.
        forecast.Decimal("budget").ShouldBe(1700m);
        forecast.GetProperty("periods").EnumerateArray().Select(p => p.Decimal("endBalance")).ShouldBe([9800m, 11400m, 13000m]);
    }

    [Fact]
    public async Task Scenarios_add_a_separate_series_without_touching_the_forecast()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, type: "Savings", initialDate: Opening);
        var scenario = (await admin.CreateAsync("/api/scenarios", new { name = "Voiture", isActive = true })).GetProperty("id").GetGuid();
        async Task AddAsync(object body) => await admin.CreateAsync($"/api/scenarios/{scenario}/events", body);
        await AddAsync(new { label = "Prime", accountId = checking, amount = 1000m, date = TestScenario.Iso(March(25)), frequency = "Once" });
        await AddAsync(new { label = "Crédit", accountId = checking, amount = -250m, date = TestScenario.Iso(April(1)), frequency = "Monthly" });
        // Hors des comptes de la prévision : sans effet.
        await AddAsync(new { label = "Livret", accountId = livret, amount = -999m, date = TestScenario.Iso(March(20)), frequency = "Once" });
        var baseline = await admin.GetJsonAsync("/api/forecast?horizon=3");

        var forecast = await admin.GetJsonAsync($"/api/forecast?horizon=3&scenarios={scenario}&scenarios={Guid.NewGuid()}");

        forecast.GetProperty("daily").GetRawText().ShouldBe(baseline.GetProperty("daily").GetRawText());
        var result = forecast.GetProperty("scenario");
        result.GetProperty("scenarioIds").EnumerateArray().Select(i => i.GetGuid()).ShouldBe([scenario]);
        result.Decimal("impact").ShouldBe(1000m - 250m - 250m);
        result.Decimal("endBalance").ShouldBe(1000m + 500m);
        result.GetProperty("daily")[0].GetProperty("date").GetString().ShouldBe("2026-03-10");
        forecast.GetProperty("periods").EnumerateArray().Select(p => p.Decimal("scenarioEndBalance")).ShouldBe([2000m, 1750m, 1500m]);

        (await admin.GetJsonAsync($"/api/forecast?scenarios={Guid.NewGuid()}")).GetProperty("scenario").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
