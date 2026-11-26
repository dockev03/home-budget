using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>
/// Valorisations (A7), patrimoine net (G5), objectifs d'épargne (J1, J2, J4) et épargne de précaution (J3) :
/// calculs, validation et visibilité (R8).
/// </summary>
public sealed class SavingsTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static string D(int month, int day) => TestScenario.Iso(new DateOnly(2026, month, day));

    private static async Task<decimal> BalanceAsync(ApiClient client, Guid accountId) =>
        (await client.GetJsonAsync($"/api/accounts/{accountId}")).Decimal("balance");

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        return (await ApiClient.ReadJsonAsync(response)).GetProperty("errors");
    }

    [Fact]
    public async Task Valuation_sets_the_balance_of_an_investment_account_and_later_movements_are_added()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var pea = await admin.CreateAccountAsync("PEA", 5000m, "Investment", Opening);
        await admin.CreateTransactionAsync(pea, 300m, date: new DateOnly(2026, 2, 15));
        (await BalanceAsync(admin, pea)).ShouldBe(5300m);

        var first = await admin.CreateAsync($"/api/accounts/{pea}/valuations", new { date = D(1, 31), value = 5100m, note = "  Relevé janvier " });
        first.GetProperty("note").GetString().ShouldBe("Relevé janvier");
        first.GetProperty("change").ValueKind.ShouldBe(JsonValueKind.Null);
        // Valorisation de janvier, puis versement du 15/02.
        (await BalanceAsync(admin, pea)).ShouldBe(5400m);

        var second = await admin.CreateAsync($"/api/accounts/{pea}/valuations", new { date = D(2, 28), value = 5600m });
        second.Decimal("change").ShouldBe(500m);
        await admin.CreateTransactionAsync(pea, 100m, date: new DateOnly(2026, 3, 5));
        var account = await admin.GetJsonAsync($"/api/accounts/{pea}");
        account.Decimal("balance").ShouldBe(5700m);
        account.Decimal("futureBalance").ShouldBe(5700m);
        (await admin.GetJsonAsync("/api/accounts")).Decimal("netWorth").ShouldBe(5700m);

        var list = await admin.GetJsonAsync($"/api/accounts/{pea}/valuations");
        list.GetArrayLength().ShouldBe(2);
        list[0].GetProperty("date").GetString().ShouldBe(D(2, 28));
        list[0].Decimal("change").ShouldBe(500m);

        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync(
            $"/api/accounts/{pea}/valuations/{Id(second)}", new { date = D(2, 28), value = 5650m }, second.Version()));
        updated.Decimal("value").ShouldBe(5650m);
        (await admin.PutAsync($"/api/accounts/{pea}/valuations/{Id(second)}", new { date = D(2, 28), value = 1m }, second.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await BalanceAsync(admin, pea)).ShouldBe(5750m);

        // Tant qu'il a des valorisations, le compte reste un compte d'investissement.
        var typeChange = await admin.PutAsync($"/api/accounts/{pea}", new
        {
            name = "PEA",
            type = "Checking",
            initialBalance = 5000m,
            initialDate = TestScenario.Iso(Opening),
        }, account.Version());
        (await ErrorsAsync(typeChange)).TryGetProperty("type", out _).ShouldBeTrue();

        (await admin.DeleteAsync($"/api/accounts/{pea}/valuations/{Id(updated)}", updated.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BalanceAsync(admin, pea)).ShouldBe(5500m);
    }

    [Fact]
    public async Task Invalid_valuations_are_rejected()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var pea = await admin.CreateAccountAsync("PEA", 5000m, "Investment", Opening);
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        await admin.CreateAsync($"/api/accounts/{pea}/valuations", new { date = D(2, 1), value = 5100m });

        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{checking}/valuations", new { date = D(2, 1), value = 1m })))
            .TryGetProperty("accountId", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{pea}/valuations", new { date = D(3, 11), value = 1m })))
            .GetProperty("date")[0].GetString()!.ShouldContain("futur");
        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{pea}/valuations", new { date = "2025-12-31", value = 1m })))
            .GetProperty("date")[0].GetString()!.ShouldContain("ouverture");
        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{pea}/valuations", new { date = D(2, 1), value = 1m })))
            .GetProperty("date")[0].GetString()!.ShouldContain("existe déjà");
        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{pea}/valuations", new { date = D(2, 2), value = 1.234m })))
            .TryGetProperty("value", out _).ShouldBeTrue();
        (await admin.PostAsync($"/api/accounts/{Guid.NewGuid()}/valuations", new { date = D(2, 2), value = 1m }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.GetJsonAsync($"/api/accounts/{pea}/valuations")).GetArrayLength().ShouldBe(1);
        (await reader.PostAsync($"/api/accounts/{pea}/valuations", new { date = D(2, 2), value = 1m })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Net_worth_history_gives_the_balance_at_the_end_of_each_period_by_account_type()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        await admin.CreateAccountAsync("Livret A", 2000m, "Savings", Opening);
        var pea = await admin.CreateAccountAsync("PEA", 5000m, "Investment", Opening);
        await admin.CreateAsync($"/api/accounts/{pea}/valuations", new { date = D(1, 31), value = 5500m });
        await admin.CreateAsync("/api/accounts", new
        {
            name = "Hors patrimoine",
            type = "Checking",
            initialBalance = 999m,
            initialDate = TestScenario.Iso(Opening),
            includeInNetWorth = false,
        });
        await admin.CreateTransactionAsync(checking, -200m, await admin.CategoryIdAsync("Courses"), new DateOnly(2026, 2, 10));
        // Opération à venir : hors du patrimoine d'aujourd'hui.
        await admin.CreateTransactionAsync(checking, -50m, await admin.CategoryIdAsync("Courses"), new DateOnly(2026, 3, 20));

        var points = (await admin.GetJsonAsync("/api/net-worth?months=12")).GetProperty("points");
        points.GetArrayLength().ShouldBe(12);
        points[0].GetProperty("key").GetString().ShouldBe("2025-04");
        points[8].Decimal("total").ShouldBe(0m);
        points[9].GetProperty("date").GetString().ShouldBe(D(1, 31));
        points[9].Decimal("total").ShouldBe(8500m);
        points[10].GetProperty("date").GetString().ShouldBe(D(2, 28));
        points[10].Decimal("total").ShouldBe(8300m);
        var last = points[11];
        last.GetProperty("key").GetString().ShouldBe("2026-03");
        last.GetProperty("date").GetString().ShouldBe(TestScenario.Iso(Today));
        last.Decimal("total").ShouldBe(8300m);
        var byType = last.GetProperty("byType").EnumerateArray().ToDictionary(t => t.GetProperty("type").GetString()!, t => t.Decimal("amount"));
        byType.ShouldBe(new Dictionary<string, decimal> { ["Checking"] = 800m, ["Savings"] = 2000m, ["Investment"] = 5500m });

        (await admin.GetJsonAsync("/api/net-worth?months=60")).GetProperty("points").GetArrayLength().ShouldBe(60);
        (await admin.GetAsync("/api/net-worth?months=7")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Goals_measure_an_account_balance_or_a_savings_category_and_advise_a_monthly_contribution()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 3000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret Maël", 2000m, "Savings", Opening);
        var child = await admin.CreateCategoryAsync("Épargne Maël", "Savings");
        await admin.CreateTransactionAsync(checking, -150m, child, new DateOnly(2026, 2, 1));
        await admin.CreateTransactionAsync(checking, -100m, child, new DateOnly(2026, 3, 1));

        var byAccount = await admin.CreateAsync("/api/savings-goals", new
        {
            name = "Livret de Maël",
            targetAmount = 5000m,
            targetDate = D(8, 31),
            accountId = livret,
        });
        byAccount.Decimal("saved").ShouldBe(2000m);
        byAccount.Decimal("remaining").ShouldBe(3000m);
        byAccount.GetProperty("percent").GetInt32().ShouldBe(40);
        byAccount.GetProperty("remainingPeriods").GetInt32().ShouldBe(6);
        byAccount.Decimal("monthlyContribution").ShouldBe(500m);
        byAccount.GetProperty("status").GetString().ShouldBe("InProgress");

        var byCategory = await admin.CreateAsync("/api/savings-goals", new
        {
            name = "Études",
            targetAmount = 100m,
            categoryId = child,
            startDate = D(2, 15),
        });
        byCategory.Decimal("saved").ShouldBe(100m);
        byCategory.GetProperty("status").GetString().ShouldBe("Reached");
        byCategory.GetProperty("monthlyContribution").ValueKind.ShouldBe(JsonValueKind.Null);

        // Sans date de début, le cumul part d'aujourd'hui.
        var fromToday = await admin.CreateAsync("/api/savings-goals", new { name = "Plus tard", targetAmount = 100m, categoryId = child });
        fromToday.GetProperty("startDate").GetString().ShouldBe(TestScenario.Iso(Today));
        fromToday.Decimal("saved").ShouldBe(0m);

        var archived = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/savings-goals/{Id(fromToday)}", new
        {
            name = "Plus tard",
            targetAmount = 100m,
            categoryId = child,
            isArchived = true,
        }, fromToday.Version()));
        archived.GetProperty("startDate").GetString().ShouldBe(TestScenario.Iso(Today));
        var list = await admin.GetJsonAsync("/api/savings-goals");
        list.EnumerateArray().Select(g => g.GetProperty("name").GetString()).ShouldBe(["Livret de Maël", "Études"]);
        (await admin.GetJsonAsync("/api/savings-goals?includeArchived=true")).GetArrayLength().ShouldBe(3);

        // Le compte et la catégorie d'un objectif ne se suppriment pas.
        var account = await admin.GetJsonAsync($"/api/accounts/{livret}");
        (await admin.DeleteAsync($"/api/accounts/{livret}", account.Version())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var unused = await admin.CreateCategoryAsync("Épargne vide", "Savings");
        var goalOnUnused = await admin.CreateAsync("/api/savings-goals", new { name = "Vide", targetAmount = 10m, categoryId = unused });
        var category = (await admin.GetJsonAsync("/api/categories")).EnumerateArray().First(c => Id(c) == unused);
        (await admin.DeleteAsync($"/api/categories/{unused}", category.Version())).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // La catégorie d'un objectif reste une catégorie d'épargne.
        var kindChange = await admin.PutAsync($"/api/categories/{unused}", new { name = "Épargne vide", kind = "Variable" }, category.Version());
        (await ErrorsAsync(kindChange)).TryGetProperty("kind", out _).ShouldBeTrue();

        // La fusion de catégories déplace les objectifs.
        (await admin.PostAsync($"/api/categories/{unused}/merge", new { targetId = child }, category.Version())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetJsonAsync("/api/savings-goals")).EnumerateArray().First(g => Id(g) == Id(goalOnUnused))
            .GetProperty("categoryId").GetGuid().ShouldBe(child);

        (await admin.DeleteAsync($"/api/savings-goals/{Id(byAccount)}", byAccount.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync("/api/audit")).GetProperty("items").EnumerateArray()
            .ShouldContain(e => e.GetProperty("target").GetString() == "SavingsGoal");
    }

    [Fact]
    public async Task Invalid_goals_are_rejected()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var livret = await admin.CreateAccountAsync("Livret A", 2000m, "Savings", Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var savings = await admin.CreateCategoryAsync("Épargne", "Savings");

        (await ErrorsAsync(await admin.PostAsync("/api/savings-goals", new { name = "A", targetAmount = 10m, accountId = livret, categoryId = savings })))
            .TryGetProperty("accountId", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.PostAsync("/api/savings-goals", new { name = "A", targetAmount = 10m })))
            .TryGetProperty("accountId", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.PostAsync("/api/savings-goals", new { name = "A", targetAmount = 10m, categoryId = groceries })))
            .GetProperty("categoryId")[0].GetString()!.ShouldContain("épargne");
        (await ErrorsAsync(await admin.PostAsync("/api/savings-goals", new { name = "A", targetAmount = 0m, accountId = livret })))
            .TryGetProperty("targetAmount", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.PostAsync("/api/savings-goals", new { name = "A", targetAmount = 10m, accountId = Guid.NewGuid() })))
            .TryGetProperty("accountId", out _).ShouldBeTrue();

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.PostAsync("/api/savings-goals", new { name = "A", targetAmount = 10m, accountId = livret })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Private_accounts_stay_out_of_the_other_members_goals_net_worth_and_emergency_fund()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var shared = await admin.CreateAccountAsync("Commun", 1000m, initialDate: Opening);
        var savings = await admin.CreateCategoryAsync("Épargne", "Savings");
        var secret = Id(await alice.CreateAsync("/api/accounts", new
        {
            name = "Livret Alice",
            type = "Savings",
            initialBalance = 4000m,
            initialDate = TestScenario.Iso(Opening),
            isPrivate = true,
        }));
        await alice.CreateTransactionAsync(secret, -300m, savings, new DateOnly(2026, 2, 1));
        await admin.CreateTransactionAsync(shared, -100m, savings, new DateOnly(2026, 2, 1));
        var aliceGoal = await alice.CreateAsync("/api/savings-goals", new { name = "Perso", targetAmount = 8000m, accountId = secret });
        await admin.CreateAsync("/api/savings-goals", new { name = "Commun", targetAmount = 1000m, categoryId = savings, startDate = D(1, 1) });

        var adminGoals = await admin.GetJsonAsync("/api/savings-goals");
        adminGoals.GetArrayLength().ShouldBe(1);
        adminGoals[0].Decimal("saved").ShouldBe(100m);
        (await alice.GetJsonAsync("/api/savings-goals")).EnumerateArray().First(g => g.GetProperty("name").GetString() == "Commun")
            .Decimal("saved").ShouldBe(400m);

        (await admin.PutAsync($"/api/savings-goals/{Id(aliceGoal)}", new { name = "X", targetAmount = 1m, accountId = shared }, aliceGoal.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorsAsync(await admin.PostAsync("/api/savings-goals", new { name = "X", targetAmount = 1m, accountId = secret })))
            .TryGetProperty("accountId", out _).ShouldBeTrue();
        (await admin.GetAsync($"/api/accounts/{secret}/valuations")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.GetJsonAsync("/api/net-worth")).GetProperty("points")[11].Decimal("total").ShouldBe(900m);
        (await alice.GetJsonAsync("/api/net-worth")).GetProperty("points")[11].Decimal("total").ShouldBe(4600m);
        (await admin.GetJsonAsync("/api/emergency-fund")).Decimal("amount").ShouldBe(0m);
        (await alice.GetJsonAsync("/api/emergency-fund")).Decimal("amount").ShouldBe(3700m);
    }

    [Fact]
    public async Task Emergency_fund_counts_the_months_of_average_expenses_covered_by_the_marked_accounts()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 5000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 3000m, "Savings", Opening);
        var pea = await admin.CreateAccountAsync("PEA", 9000m, "Investment", Opening);
        await admin.CreateAsync("/api/accounts", new
        {
            name = "Livret projet",
            type = "Savings",
            initialBalance = 7000m,
            initialDate = TestScenario.Iso(Opening),
            isEmergencyFund = false,
        });
        (await admin.GetJsonAsync($"/api/accounts/{livret}")).GetProperty("isEmergencyFund").GetBoolean().ShouldBeTrue();
        (await admin.GetJsonAsync($"/api/accounts/{pea}")).GetProperty("isEmergencyFund").GetBoolean().ShouldBeFalse();
        (await admin.GetJsonAsync($"/api/accounts/{checking}")).GetProperty("isEmergencyFund").GetBoolean().ShouldBeFalse();

        var groceries = await admin.CategoryIdAsync("Courses");
        var internet = await admin.CategoryIdAsync("Internet");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        await admin.CreateTransactionAsync(checking, -1000m, groceries, new DateOnly(2026, 1, 5));
        await admin.CreateTransactionAsync(checking, -1000m, groceries, new DateOnly(2026, 2, 5));
        await admin.CreateTransactionAsync(checking, -500m, internet, new DateOnly(2026, 2, 6));
        await admin.CreateTransactionAsync(checking, 2500m, salary, new DateOnly(2026, 2, 1));
        // La période en cours n'entre pas dans la moyenne.
        await admin.CreateTransactionAsync(checking, -900m, groceries, new DateOnly(2026, 3, 2));

        // Six périodes demandées, mais deux seulement depuis la première opération : (1 000 + 1 500) / 2.
        var fund = await admin.GetJsonAsync("/api/emergency-fund");
        fund.Decimal("amount").ShouldBe(3000m);
        fund.Decimal("monthlyExpenses").ShouldBe(1250m);
        fund.GetProperty("averageMonths").GetInt32().ShouldBe(2);
        fund.Decimal("monthsCovered").ShouldBe(2.4m);
        fund.GetProperty("accounts").EnumerateArray().Select(a => Id(a)).ShouldBe([livret]);

        var settings = await admin.GetJsonAsync("/api/settings");
        (await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 80,
            budgetAlertPercent = 100,
            emergencyFundAverageMonths = 1,
        }, settings.Version())).EnsureSuccessStatusCode();
        fund = await admin.GetJsonAsync("/api/emergency-fund");
        fund.Decimal("monthlyExpenses").ShouldBe(1500m);
        fund.Decimal("monthsCovered").ShouldBe(2m);
    }

    [Fact]
    public async Task Emergency_fund_without_history_covers_nothing_measurable()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Livret A", 3000m, "Savings", Opening);

        var fund = await admin.GetJsonAsync("/api/emergency-fund");
        fund.Decimal("amount").ShouldBe(3000m);
        fund.Decimal("monthlyExpenses").ShouldBe(0m);
        fund.GetProperty("averageMonths").GetInt32().ShouldBe(0);
        fund.GetProperty("monthsCovered").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
