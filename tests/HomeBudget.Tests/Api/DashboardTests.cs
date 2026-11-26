using System.Diagnostics;
using System.Net;
using System.Text.Json;

using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

public sealed class DashboardTests
{
    private static readonly DateOnly Opening = new(2025, 1, 1);

    private static DateOnly March(int day) => new(2026, 3, day);

    [Fact]
    public async Task Dashboard_gives_key_figures_expenses_budget_and_twelve_periods()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, type: "Savings", initialDate: Opening);
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");
        var internet = await admin.CategoryIdAsync("Internet");
        var savings = await admin.CreateCategoryAsync("Épargne Livret", "Savings");

        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null);
        await admin.CreateTransactionAsync(checking, 2000m, salary, March(1));
        await admin.CreateTransactionAsync(checking, -100m, groceries, March(2));
        await admin.CreateTransactionAsync(checking, -30m, internet, March(3));
        await admin.CreateTransactionAsync(checking, -5m, null, March(4)); // sans catégorie : hors chiffres
        await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = checking,
            toAccountId = livret,
            date = TestScenario.Iso(March(5)),
            amount = 200m,
            categoryId = savings,
        });
        await admin.CreateTransactionAsync(checking, -60m, groceries, new DateOnly(2026, 2, 14));
        await admin.CreateTransactionAsync(checking, -999m, groceries, new DateOnly(2025, 3, 31)); // 13 périodes avant : hors historique

        var dashboard = await admin.GetJsonAsync("/api/dashboard/2026-03");

        dashboard.GetProperty("period").GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
        var figures = dashboard.GetProperty("keyFigures");
        figures.Decimal("income").ShouldBe(2000m);
        figures.Decimal("expenses").ShouldBe(130m);
        figures.Decimal("savings").ShouldBe(200m);
        // Courant seulement (le livret n'est pas dans la prévision) : 1000 − 999 − 60 + 2000 − 100 − 30 − 5 − 200.
        figures.Decimal("balance").ShouldBe(1606m);
        figures.GetProperty("balanceDate").GetString().ShouldBe("2026-03-10");
        // Prévision : il reste 300 € de courses au budget.
        figures.Decimal("forecastBalance").ShouldBe(1306m);
        figures.GetProperty("remainingToLive").ValueKind.ShouldBe(JsonValueKind.Number);

        var byCategory = dashboard.GetProperty("expensesByCategory").EnumerateArray().ToList();
        byCategory.Select(c => c.GetProperty("name").GetString()).ShouldBe(["Courses", "Internet"]);
        byCategory[0].Decimal("amount").ShouldBe(100m);

        var budget = dashboard.GetProperty("budgetVsActual").EnumerateArray().ToList();
        var courses = budget.Single(b => b.GetProperty("name").GetString() == "Courses");
        courses.Decimal("budgeted").ShouldBe(400m);
        courses.Decimal("actual").ShouldBe(100m);
        courses.GetProperty("status").GetString().ShouldBe("Ok");
        budget.Select(b => b.GetProperty("name").GetString()).ShouldBe(["Internet", "Courses"]); // les autres n'ont ni budget ni réel

        var history = dashboard.GetProperty("history").EnumerateArray().ToList();
        history.Count.ShouldBe(12);
        history[0].GetProperty("key").GetString().ShouldBe("2025-04");
        history[^1].GetProperty("key").GetString().ShouldBe("2026-03");
        history[^1].Decimal("income").ShouldBe(2000m);
        history[^1].Decimal("expenses").ShouldBe(130m);
        history[^1].Decimal("savings").ShouldBe(200m);
        history[^2].Decimal("expenses").ShouldBe(60m);
        history.Take(10).ShouldAllBe(h => h.Decimal("income") == 0 && h.Decimal("expenses") == 0);
    }

    [Fact]
    public async Task Past_period_shows_its_closing_balance_and_no_forecast()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        await admin.CreateTransactionAsync(checking, -100m, await admin.CategoryIdAsync("Courses"), new DateOnly(2026, 2, 10));
        await admin.CreateTransactionAsync(checking, -50m, null, March(2));

        var past = (await admin.GetJsonAsync("/api/dashboard/2026-02")).GetProperty("keyFigures");
        past.Decimal("balance").ShouldBe(900m);
        past.GetProperty("balanceDate").GetString().ShouldBe("2026-02-28");
        past.GetProperty("forecastBalance").ValueKind.ShouldBe(JsonValueKind.Null);
        past.GetProperty("remainingToLive").ValueKind.ShouldBe(JsonValueKind.Null);

        var future = (await admin.GetJsonAsync("/api/dashboard/2026-05")).GetProperty("keyFigures");
        future.GetProperty("balance").ValueKind.ShouldBe(JsonValueKind.Null);

        (await admin.GetAsync("/api/dashboard/mars")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_follows_fixed_day_periods()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 11, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 0m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var version = (await admin.GetJsonAsync("/api/settings")).Version();
        (await admin.PutAsync("/api/settings", new { periodMode = "FixedDay", periodStartDay = 25, budgetWarningPercent = 80, budgetAlertPercent = 100 }, version))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        // Période « novembre » : du 25 octobre au 24 novembre.
        await admin.CreateTransactionAsync(account, -10m, groceries, new DateOnly(2026, 10, 24));
        await admin.CreateTransactionAsync(account, -20m, groceries, new DateOnly(2026, 10, 25));
        await admin.CreateTransactionAsync(account, -40m, groceries, new DateOnly(2026, 11, 24));
        await admin.CreateTransactionAsync(account, -80m, groceries, new DateOnly(2026, 11, 25));

        var history = (await admin.GetJsonAsync("/api/dashboard/2026-12")).GetProperty("history").EnumerateArray().ToList();

        history[^1].GetProperty("key").GetString().ShouldBe("2026-12");
        history[^1].Decimal("expenses").ShouldBe(80m);
        history[^2].GetProperty("start").GetString().ShouldBe("2026-10-25");
        history[^2].Decimal("expenses").ShouldBe(60m);
        history[^3].Decimal("expenses").ShouldBe(10m);
    }

    [Fact]
    public async Task Dashboard_stays_under_500_ms_with_30000_transactions()
    {
        var today = March(10);
        await using var factory = new HomeBudgetFactory { Today = today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var categories = (await admin.GetJsonAsync("/api/categories")).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var random = new Random(42);
            for (var i = 0; i < 30_000; i++)
            {
                var transaction = new Transaction { AccountId = checking, Date = today.AddDays(-random.Next(0, 3 * 365)) };
                transaction.SetLines([new TransactionLine { CategoryId = categories[random.Next(categories.Count)], Amount = -random.Next(1, 20_000) / 100m }]);
                db.Transactions.Add(transaction);
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            (await db.Transactions.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(30_000);
        }

        (await admin.GetAsync("/api/dashboard/2026-03")).EnsureSuccessStatusCode(); // préparation des requêtes
        var watch = Stopwatch.StartNew();
        (await admin.GetAsync("/api/dashboard/2026-03")).EnsureSuccessStatusCode();
        watch.Stop();

        watch.ElapsedMilliseconds.ShouldBeLessThan(500);
    }

    [Fact]
    public async Task Comparison_puts_the_period_next_to_the_previous_one_and_the_same_one_a_year_before()
    {
        await using var factory = new HomeBudgetFactory { Today = March(10) };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, type: "Savings", initialDate: Opening);
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");
        var restaurants = await admin.CategoryIdAsync("Restaurants");
        var cinema = await admin.CreateCategoryAsync("Cinéma", "Variable", restaurants);

        await admin.CreateTransactionAsync(checking, 2000m, salary, March(1));
        await admin.CreateTransactionAsync(checking, -100m, groceries, March(2));
        await admin.CreateTransactionAsync(checking, -20m, cinema, March(3));
        await admin.CreateTransactionAsync(checking, 1800m, salary, new DateOnly(2026, 2, 1));
        await admin.CreateTransactionAsync(checking, -300m, groceries, new DateOnly(2026, 2, 5));
        await admin.CreateTransactionAsync(checking, -250m, groceries, new DateOnly(2025, 3, 5));
        await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = checking,
            toAccountId = livret,
            date = TestScenario.Iso(March(4)),
            amount = 100m,
            label = "Virement",
        });

        var comparison = await admin.GetJsonAsync("/api/dashboard/2026-03/comparison");

        comparison.GetProperty("period").GetProperty("key").GetString().ShouldBe("2026-03");
        var current = comparison.GetProperty("current");
        current.Decimal("income").ShouldBe(2000m);
        current.Decimal("variable").ShouldBe(120m);
        current.Decimal("savings").ShouldBe(0m); // virement sans catégorie : exclu (R5)
        comparison.GetProperty("previous").GetProperty("key").GetString().ShouldBe("2026-02");
        comparison.GetProperty("previous").Decimal("variable").ShouldBe(300m);
        comparison.GetProperty("lastYear").GetProperty("key").GetString().ShouldBe("2025-03");
        comparison.GetProperty("lastYear").Decimal("income").ShouldBe(0m);

        var lines = comparison.GetProperty("categories").EnumerateArray().ToDictionary(c => c.GetProperty("categoryId").GetGuid());
        lines.Keys.ShouldBe([salary, groceries, restaurants], ignoreOrder: true);
        lines[groceries].Decimal("current").ShouldBe(100m);
        lines[groceries].Decimal("previous").ShouldBe(300m);
        lines[groceries].Decimal("lastYear").ShouldBe(250m);
        // La sous-catégorie est comptée dans sa catégorie principale.
        lines[restaurants].Decimal("current").ShouldBe(20m);

        (await admin.GetAsync("/api/dashboard/mars/comparison")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
