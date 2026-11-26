using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Budget d'une période (D4), copie (D5), provisions (D11) et récapitulatif annuel (P3).</summary>
public sealed class AdvancedBudgetTests
{
    private static readonly DateOnly Opening = new(2025, 1, 1);

    private static JsonElement Line(JsonElement budget, Guid categoryId) =>
        budget.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("categoryId").GetGuid() == categoryId);

    private static JsonElement Proposal(JsonElement proposals, Guid categoryId) =>
        proposals.EnumerateArray().First(c => c.GetProperty("categoryId").GetGuid() == categoryId);

    [Fact]
    public async Task Period_budget_replaces_the_default_for_that_period_only()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 3, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var groceries = await admin.CategoryIdAsync("Courses");
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null)).EnsureSuccessStatusCode();

        var response = await admin.PutAsync($"/api/budgets/2026-03/categories/{groceries}", new { mode = "Amount", value = 650m }, null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await ApiClient.ReadJsonAsync(response);

        var march = Line(await admin.GetJsonAsync("/api/budgets/2026-03"), groceries);
        march.Decimal("budgeted").ShouldBe(650m);
        march.Decimal("value").ShouldBe(400m);
        march.GetProperty("periodBudget").Decimal("value").ShouldBe(650m);
        var april = Line(await admin.GetJsonAsync("/api/budgets/2026-04"), groceries);
        april.Decimal("budgeted").ShouldBe(400m);
        april.GetProperty("periodBudget").ValueKind.ShouldBe(JsonValueKind.Null);

        // Concurrence optimiste comme pour le budget par défaut.
        (await admin.PutAsync($"/api/budgets/2026-03/categories/{groceries}", new { mode = "Amount", value = 1m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await admin.PutAsync($"/api/budgets/2026-13/categories/{groceries}", new { mode = "Amount", value = 1m }, null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.DeleteAsync($"/api/budgets/2026-03/categories/{groceries}", created.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Line(await admin.GetJsonAsync("/api/budgets/2026-03"), groceries).Decimal("budgeted").ShouldBe(400m);
    }

    [Fact]
    public async Task Copy_from_previous_period_or_from_the_average_with_preview()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 4, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var fuel = await admin.CategoryIdAsync("Essence");
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null)).EnsureSuccessStatusCode();
        (await admin.PutAsync($"/api/budgets/2026-03/categories/{groceries}", new { mode = "Amount", value = 550m }, null)).EnsureSuccessStatusCode();

        // Période précédente : seul le budget propre de mars diffère du budget appliqué en avril.
        var preview = await ApiClient.ReadJsonAsync(await admin.PostAsync("/api/budgets/2026-04/copy/preview", new { source = "PreviousPeriod" }));
        var proposal = preview.EnumerateArray().ShouldHaveSingleItem();
        proposal.GetProperty("categoryId").GetGuid().ShouldBe(groceries);
        proposal.Decimal("currentValue").ShouldBe(400m);
        proposal.Decimal("proposedValue").ShouldBe(550m);
        Line(await admin.GetJsonAsync("/api/budgets/2026-04"), groceries).Decimal("budgeted").ShouldBe(400m); // aperçu sans effet

        var applied = await admin.CreateAsync("/api/budgets/2026-04/copy", new { source = "PreviousPeriod" });
        applied.GetProperty("applied").GetInt32().ShouldBe(1);
        Line(await admin.GetJsonAsync("/api/budgets/2026-04"), groceries).Decimal("budgeted").ShouldBe(550m);

        // Moyenne des 3 périodes précédentes (janvier à mars), réalisé propre, en montant.
        await admin.CreateTransactionAsync(account, -300m, groceries, new DateOnly(2026, 1, 5));
        await admin.CreateTransactionAsync(account, -200m, groceries, new DateOnly(2026, 2, 5));
        await admin.CreateTransactionAsync(account, -100m, groceries, new DateOnly(2026, 3, 5));
        await admin.CreateTransactionAsync(account, -50m, fuel, new DateOnly(2026, 3, 6));
        await admin.CreateTransactionAsync(account, -999m, fuel, new DateOnly(2025, 12, 31)); // hors moyenne

        var average = await ApiClient.ReadJsonAsync(await admin.PostAsync("/api/budgets/2026-04/copy/preview", new { source = "Average", months = 3 }));
        Proposal(average, groceries).Decimal("proposedValue").ShouldBe(200m);
        Proposal(average, fuel).Decimal("proposedValue").ShouldBe(16.67m);
        Proposal(average, fuel).GetProperty("currentMode").ValueKind.ShouldBe(JsonValueKind.Null);

        var onlyFuel = await admin.CreateAsync("/api/budgets/2026-04/copy", new { source = "Average", months = 3, categoryIds = new[] { fuel } });
        onlyFuel.GetProperty("applied").GetInt32().ShouldBe(1);
        var april = await admin.GetJsonAsync("/api/budgets/2026-04");
        Line(april, fuel).Decimal("budgeted").ShouldBe(16.67m);
        Line(april, groceries).Decimal("budgeted").ShouldBe(550m);

        (await admin.PostAsync("/api/budgets/2026-04/copy/preview", new { source = "Average", months = 0 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Provision_is_spread_in_the_budget_and_counted_in_the_forecast_only_when_due()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 3, 10) };
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Courant", 3000m, initialDate: Opening);
        var taxes = await admin.CategoryIdAsync("Impôts");

        var provision = await admin.CreateAsync("/api/provisions", new
        {
            label = "Taxe foncière",
            categoryId = taxes,
            amount = 900m,
            dueDate = TestScenario.Iso(new DateOnly(2026, 5, 15)),
            isAnnual = false,
            startDate = TestScenario.Iso(new DateOnly(2026, 3, 1)),
        });
        provision.Decimal("share").ShouldBe(300m);
        provision.Decimal("saved").ShouldBe(300m);
        provision.GetProperty("periods").GetInt32().ShouldBe(3);
        provision.GetProperty("dueKey").GetString().ShouldBe("2026-05");

        var march = await admin.GetJsonAsync("/api/budgets/2026-03");
        Line(march, taxes).Decimal("budgeted").ShouldBe(300m);
        Line(march, taxes).Decimal("provisioned").ShouldBe(300m);
        march.GetProperty("budgeted").Decimal("fixed").ShouldBe(300m);
        var may = await admin.GetJsonAsync("/api/budgets/2026-05");
        Line(may, taxes).Decimal("budgeted").ShouldBe(900m);
        may.GetProperty("budgeted").Decimal("fixed").ShouldBe(300m);
        Line(await admin.GetJsonAsync("/api/budgets/2026-06"), taxes).Decimal("budgeted").ShouldBe(0m);

        // La part réservée reste sur le compte : la prévision de mars n'en retire rien.
        (await admin.GetJsonAsync("/api/forecast")).Decimal("endBalance").ShouldBe(3000m);

        // Une provision porte sur une dépense, et son échéance suit le début du lissage.
        (await admin.PostAsync("/api/provisions", new { label = "X", categoryId = await admin.CategoryIdAsync("Primes"), amount = 10m, dueDate = "2026-05-01", isAnnual = false }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/provisions", new { label = "X", categoryId = taxes, amount = 10m, dueDate = "2026-01-01", isAnnual = false, startDate = "2026-03-01" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Une catégorie qui porte une provision ne se supprime pas.
        var category = await admin.CreateCategoryAsync("Travaux", "Variable");
        var travaux = await admin.CreateAsync("/api/provisions", new { label = "Toiture", categoryId = category, amount = 5000m, dueDate = "2027-03-01", isAnnual = false });
        var categoryVersion = (await admin.GetJsonAsync("/api/categories")).EnumerateArray().First(c => c.GetProperty("id").GetGuid() == category).Version();
        (await admin.DeleteAsync($"/api/categories/{category}", categoryVersion)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/provisions/{travaux.GetProperty("id").GetGuid()}", travaux.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync("/api/provisions")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Provision_due_this_period_is_taken_out_by_the_forecast()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 5, 10) };
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Courant", 3000m, initialDate: Opening);
        var taxes = await admin.CategoryIdAsync("Impôts");
        await admin.CreateAsync("/api/provisions", new
        {
            label = "Taxe foncière",
            categoryId = taxes,
            amount = 900m,
            dueDate = TestScenario.Iso(new DateOnly(2026, 5, 15)),
            isAnnual = false,
            startDate = TestScenario.Iso(new DateOnly(2026, 3, 1)),
        });

        (await admin.GetJsonAsync("/api/forecast")).Decimal("endBalance").ShouldBe(2100m);
    }

    [Fact]
    public async Task Annual_report_compares_each_category_with_the_previous_year()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 6, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 0m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var restaurants = await admin.CategoryIdAsync("Restaurants");
        var cinema = await admin.CreateCategoryAsync("Cinéma", "Variable", restaurants);
        var salary = await admin.CategoryIdAsync("Salaire Kévin");

        await admin.CreateTransactionAsync(account, -400m, groceries, new DateOnly(2025, 3, 1));
        await admin.CreateTransactionAsync(account, -500m, groceries, new DateOnly(2026, 2, 1));
        await admin.CreateTransactionAsync(account, -30m, cinema, new DateOnly(2026, 2, 2));
        await admin.CreateTransactionAsync(account, -20m, restaurants, new DateOnly(2026, 12, 31));
        await admin.CreateTransactionAsync(account, 2000m, salary, new DateOnly(2026, 1, 1));
        await admin.CreateTransactionAsync(account, -7m, null, new DateOnly(2026, 1, 2));
        await admin.CreateTransactionAsync(account, -1000m, groceries, new DateOnly(2027, 1, 1)); // année suivante

        var report = await admin.GetJsonAsync("/api/reports/annual/2026");

        report.GetProperty("start").GetString().ShouldBe("2026-01-01");
        report.GetProperty("end").GetString().ShouldBe("2026-12-31");
        var lines = report.GetProperty("categories");
        var g = Line(report, groceries);
        g.Decimal("actual").ShouldBe(500m);
        g.Decimal("previousActual").ShouldBe(400m);
        g.Decimal("difference").ShouldBe(100m);
        g.Decimal("changePercent").ShouldBe(25m);
        Line(report, restaurants).Decimal("actual").ShouldBe(50m); // sous-catégorie comprise
        Line(report, restaurants).GetProperty("changePercent").ValueKind.ShouldBe(JsonValueKind.Null);
        Line(report, cinema).Decimal("actual").ShouldBe(30m);
        lines.EnumerateArray().ShouldNotContain(l => l.GetProperty("name").GetString() == "Essence");
        report.GetProperty("actual").Decimal("income").ShouldBe(2000m);
        report.GetProperty("actual").Decimal("variable").ShouldBe(550m);
        report.GetProperty("actual").Decimal("remaining").ShouldBe(1450m);
        report.GetProperty("previous").Decimal("variable").ShouldBe(400m);
        report.Decimal("uncategorized").ShouldBe(-7m);

        (await admin.GetAsync("/api/reports/annual/99999")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recurrence_due_on_a_weekend_is_moved_to_the_next_business_day()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 4, 25) };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 0m, initialDate: Opening);
        var request = new
        {
            label = "Loyer",
            accountId = account,
            categoryId = await admin.CategoryIdAsync("Prêt Immo"),
            amount = -800m,
            mode = "ToValidate",
            // Le 2 mai 2026 est un samedi, le 1er mai férié : report au lundi 4.
            startDate = "2026-05-02",
            unit = "Month",
            interval = 1,
            end = "Never",
            isInstallment = false,
            businessDayShift = "Next",
        };

        var recurrence = await admin.CreateAsync("/api/recurrences", request);

        recurrence.GetProperty("businessDayShift").GetString().ShouldBe("Next");
        recurrence.GetProperty("nextDate").GetString().ShouldBe("2026-05-04");
        (await admin.PostAsync("/api/recurrences", request with { unit = "Day", interval = 2 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
