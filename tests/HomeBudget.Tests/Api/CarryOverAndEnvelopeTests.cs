using System.Net;
using System.Text.Json;

using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

/// <summary>Report du reste (D6) et mode enveloppes (D7).</summary>
public sealed class CarryOverAndEnvelopeTests
{
    private static readonly DateOnly Opening = new(2025, 1, 1);

    private static JsonElement Line(JsonElement budget, Guid categoryId) =>
        budget.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("categoryId").GetGuid() == categoryId);

    [Fact]
    public async Task Remainder_is_carried_over_period_after_period_positive_or_negative()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 4, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 3000m, initialDate: Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null)).EnsureSuccessStatusCode();
        await admin.CreateTransactionAsync(account, -300m, groceries, new DateOnly(2026, 2, 5));
        await admin.CreateTransactionAsync(account, -550m, groceries, new DateOnly(2026, 3, 5));
        await admin.CreateTransactionAsync(account, -999m, groceries, new DateOnly(2026, 1, 5)); // avant l'activation

        var enabled = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/budgets/categories/{groceries}/carry-over", new { enabled = true, since = "2026-02" }, null));
        enabled.GetProperty("since").GetString().ShouldBe("2026-02");

        // Février : rien à reporter. Mars : +100 de février. Avril : +100 − 150 = −50.
        Line(await admin.GetJsonAsync("/api/budgets/2026-02"), groceries).Decimal("carriedOver").ShouldBe(0m);
        var march = Line(await admin.GetJsonAsync("/api/budgets/2026-03"), groceries);
        march.Decimal("carriedOver").ShouldBe(100m);
        march.Decimal("budgeted").ShouldBe(500m);
        var aprilBudget = await admin.GetJsonAsync("/api/budgets/2026-04");
        var april = Line(aprilBudget, groceries);
        april.Decimal("carriedOver").ShouldBe(-50m);
        april.Decimal("budgeted").ShouldBe(350m);
        april.GetProperty("carryOverSince").GetString().ShouldBe("2026-02");
        aprilBudget.GetProperty("budgeted").Decimal("variable").ShouldBe(400m); // hors récapitulatif
        aprilBudget.GetProperty("envelope").ValueKind.ShouldBe(JsonValueKind.Null);

        // Une période à venir ne reçoit pas de report : la prévision dépense déjà le reste d'avril.
        Line(await admin.GetJsonAsync("/api/budgets/2026-05"), groceries).Decimal("carriedOver").ShouldBe(0m);
        // 3000 − 999 − 300 − 550 = 1151, moins le budget d'avril réduit du dépassement (350).
        (await admin.GetJsonAsync("/api/forecast")).Decimal("endBalance").ShouldBe(801m);

        // Seules les dépenses reportent, et pas depuis une période à venir.
        (await admin.PutAsync($"/api/budgets/categories/{await admin.CategoryIdAsync("Primes")}/carry-over", new { enabled = true }, null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/budgets/categories/{groceries}/carry-over", new { enabled = true, since = "2026-05" }, null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/budgets/categories/{Guid.NewGuid()}/carry-over", new { enabled = false }, null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Désactivé, puis réactivé sans période : il repart de la période en cours.
        (await admin.PutAsync($"/api/budgets/categories/{groceries}/carry-over", new { enabled = false }, null)).EnsureSuccessStatusCode();
        Line(await admin.GetJsonAsync("/api/budgets/2026-04"), groceries).Decimal("carriedOver").ShouldBe(0m);
        var again = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/budgets/categories/{groceries}/carry-over", new { enabled = true }, null));
        again.GetProperty("since").GetString().ShouldBe("2026-04");

        // Un lecteur ne change pas le report.
        var viewer = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await viewer.PutAsync($"/api/budgets/categories/{groceries}/carry-over", new { enabled = false }, null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Envelope_mode_accumulates_what_is_left_to_assign_and_carries_every_expense()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 4, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 0m, initialDate: Opening);
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var groceries = await admin.CategoryIdAsync("Courses");
        var fuel = await admin.CategoryIdAsync("Essence");
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 400m }, null)).EnsureSuccessStatusCode();
        (await admin.PutAsync($"/api/budgets/categories/{fuel}", new { mode = "Amount", value = 100m }, null)).EnsureSuccessStatusCode();

        // Activé depuis les paramètres : il démarre à la période en cours.
        var settings = await admin.GetJsonAsync("/api/settings");
        settings.GetProperty("envelopeSince").ValueKind.ShouldBe(JsonValueKind.Null);
        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 80,
            budgetAlertPercent = 100,
            envelopeMode = true,
        }, settings.Version()));
        updated.GetProperty("envelopeSince").GetString().ShouldBe("2026-04");

        // Pour vérifier le cumul, on fait remonter l'activation à mars.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.HouseholdSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.EnvelopeSince, "2026-03"), TestContext.Current.CancellationToken);
        }
        await admin.CreateTransactionAsync(account, 2000m, salary, new DateOnly(2026, 3, 1));
        await admin.CreateTransactionAsync(account, -300m, groceries, new DateOnly(2026, 3, 5));
        await admin.CreateTransactionAsync(account, 1800m, salary, new DateOnly(2026, 4, 2));

        var budget = await admin.GetJsonAsync("/api/budgets/2026-04");
        var envelope = budget.GetProperty("envelope");
        envelope.GetProperty("since").GetString().ShouldBe("2026-03");
        envelope.Decimal("income").ShouldBe(1800m);
        envelope.Decimal("assigned").ShouldBe(500m);
        envelope.Decimal("toAssign").ShouldBe(2800m); // (2000 − 500) + (1800 − 500)
        // Chaque dépense reporte son reste, sans report activé à la main.
        Line(budget, groceries).Decimal("carriedOver").ShouldBe(100m);
        Line(budget, fuel).Decimal("carriedOver").ShouldBe(100m);
        Line(budget, fuel).GetProperty("carryOverSince").ValueKind.ShouldBe(JsonValueKind.Null);
        (await admin.GetJsonAsync("/api/budgets/2026-02")).GetProperty("envelope").ValueKind.ShouldBe(JsonValueKind.Null);

        // Déplacer d'une enveloppe à l'autre ne change pas « à affecter ».
        var moved = await ApiClient.ReadJsonAsync(await admin.PostAsync("/api/budgets/2026-04/moves", new { fromCategoryId = fuel, toCategoryId = groceries, amount = 60m }));
        moved.GetProperty("from").Decimal("value").ShouldBe(40m);
        moved.GetProperty("to").Decimal("value").ShouldBe(460m);
        budget = await admin.GetJsonAsync("/api/budgets/2026-04");
        budget.GetProperty("envelope").Decimal("toAssign").ShouldBe(2800m);
        Line(budget, groceries).Decimal("budgeted").ShouldBe(560m);
        Line(budget, fuel).Decimal("budgeted").ShouldBe(140m);
        Line(await admin.GetJsonAsync("/api/budgets/2026-05"), fuel).Decimal("budgeted").ShouldBe(100m); // période seule

        // Le reste reporté ne se déplace pas ; ni un revenu, ni la même catégorie.
        (await admin.PostAsync("/api/budgets/2026-04/moves", new { fromCategoryId = fuel, toCategoryId = groceries, amount = 41m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/budgets/2026-04/moves", new { fromCategoryId = salary, toCategoryId = groceries, amount = 1m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/budgets/2026-04/moves", new { fromCategoryId = fuel, toCategoryId = fuel, amount = 1m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/budgets/2026-04/moves", new { fromCategoryId = fuel, toCategoryId = groceries, amount = 0m }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Désactivé : plus d'« à affecter » ni de report.
        var current = await admin.GetJsonAsync("/api/settings");
        (await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 80,
            budgetAlertPercent = 100,
            envelopeMode = false,
        }, current.Version())).EnsureSuccessStatusCode();
        budget = await admin.GetJsonAsync("/api/budgets/2026-04");
        budget.GetProperty("envelope").ValueKind.ShouldBe(JsonValueKind.Null);
        Line(budget, groceries).Decimal("carriedOver").ShouldBe(0m);
    }

    [Fact]
    public async Task Settings_update_without_envelope_mode_keeps_it()
    {
        await using var factory = new HomeBudgetFactory { Today = new DateOnly(2026, 4, 10) };
        var admin = await factory.CreateAdminClientAsync();
        var settings = await admin.GetJsonAsync("/api/settings");
        settings = await ApiClient.ReadJsonAsync(await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 80,
            budgetAlertPercent = 100,
            envelopeMode = true,
        }, settings.Version()));
        settings = await ApiClient.ReadJsonAsync(await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 70,
            budgetAlertPercent = 100,
        }, settings.Version()));
        settings.GetProperty("envelopeSince").GetString().ShouldBe("2026-04");
    }
}
