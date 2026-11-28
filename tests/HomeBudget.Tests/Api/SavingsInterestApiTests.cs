using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Taux des livrets et intérêts par quinzaine (A9).</summary>
public sealed class SavingsInterestApiTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Opening = new(2025, 12, 31);

    private static object Rate(string from, decimal rate) => new { effectiveFrom = from, annualRate = rate };

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        return (await ApiClient.ReadJsonAsync(response)).GetProperty("errors");
    }

    [Fact]
    public async Task Rates_give_the_earned_and_projected_interest_by_fortnight()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var livret = await admin.CreateAccountAsync("Livret A", 1000m, "Savings", Opening);

        // À 2,4 %, 1 000 € rapportent 1 € par quinzaine ; le taux double au 1er juillet (changement programmé).
        await admin.CreateAsync($"/api/accounts/{livret}/savings-rates", Rate("2025-01-01", 2.4m));
        var scheduled = await admin.CreateAsync($"/api/accounts/{livret}/savings-rates", Rate("2026-07-01", 4.8m));
        scheduled.GetProperty("isScheduled").GetBoolean().ShouldBeTrue();
        // Dépôt le 10 février (rapporte dès le 16), retrait futur le 20 avril (ne rapporte plus dès le 16).
        await admin.CreateTransactionAsync(livret, 1000m, date: new DateOnly(2026, 2, 10));
        await admin.CreateTransactionAsync(livret, -500m, date: new DateOnly(2026, 4, 20));

        var interest = await admin.GetJsonAsync($"/api/accounts/{livret}/interest");
        interest.GetProperty("year").GetInt32().ShouldBe(2026);
        interest.Decimal("currentRate").ShouldBe(2.4m);
        interest.GetProperty("nextRate").Decimal("annualRate").ShouldBe(4.8m);
        // Quinzaines terminées au 10 mars : 1 + 1 + 1 + 2.
        interest.Decimal("earned").ShouldBe(5m);
        // Puis 2 + 2 + 2, 1,5 × 5 et 3 × 12 au double taux.
        interest.Decimal("projected").ShouldBe(54.5m);
        var fortnights = interest.GetProperty("fortnights");
        fortnights.GetArrayLength().ShouldBe(24);
        fortnights[3].Decimal("base").ShouldBe(2000m);
        fortnights[3].GetProperty("isEnded").GetBoolean().ShouldBeTrue();
        fortnights[4].GetProperty("isEnded").GetBoolean().ShouldBeFalse();
        fortnights[7].Decimal("base").ShouldBe(1500m);
        fortnights[12].Decimal("annualRate").ShouldBe(4.8m);

        // L'année d'ouverture : le solde initial du 31 décembre n'a rien rapporté.
        (await admin.GetJsonAsync($"/api/accounts/{livret}/interest?year=2025")).Decimal("projected").ShouldBe(0m);

        var account = await admin.GetJsonAsync($"/api/accounts/{livret}");
        account.Decimal("savingsRate").ShouldBe(2.4m);
        account.Decimal("interestEarned").ShouldBe(5m);
        // Indicatif : le solde ne bouge pas.
        account.Decimal("balance").ShouldBe(2000m);

        var rates = await admin.GetJsonAsync($"/api/accounts/{livret}/savings-rates");
        rates.GetArrayLength().ShouldBe(2);
        rates[0].GetProperty("effectiveFrom").GetString().ShouldBe("2026-07-01");

        // Un changement programmé se modifie (avec la version) ou se supprime.
        (await admin.PutAsync($"/api/accounts/{livret}/savings-rates/{scheduled.GetProperty("id")}", Rate("2026-08-01", 1.7m), null))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        var moved = await ApiClient.ReadJsonAsync(
            await admin.PutAsync($"/api/accounts/{livret}/savings-rates/{scheduled.GetProperty("id")}", Rate("2026-08-01", 1.7m), scheduled.Version()));
        moved.Decimal("annualRate").ShouldBe(1.7m);
        (await admin.DeleteAsync($"/api/accounts/{livret}/savings-rates/{moved.GetProperty("id")}", moved.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var flat = await admin.GetJsonAsync($"/api/accounts/{livret}/interest");
        flat.GetProperty("nextRate").ValueKind.ShouldBe(JsonValueKind.Null);
        flat.Decimal("projected").ShouldBe(5m + 6m + (1.5m * 17));

        // Tant qu'il a des taux, le compte reste un compte d'épargne.
        var typeChange = await admin.PutAsync($"/api/accounts/{livret}", new
        {
            name = "Livret A",
            type = "Checking",
            initialBalance = 1000m,
            initialDate = TestScenario.Iso(Opening),
        }, account.Version());
        (await ErrorsAsync(typeChange)).TryGetProperty("type", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_rates_are_rejected_and_private_rates_stay_private()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("LDDS", 1000m, "Savings", Opening);

        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{checking}/savings-rates", Rate("2026-01-01", 1.7m))))
            .TryGetProperty("accountId", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.GetAsync($"/api/accounts/{checking}/interest"))).TryGetProperty("accountId", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.GetAsync($"/api/accounts/{livret}/interest?year=2300"))).TryGetProperty("year", out _).ShouldBeTrue();
        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{livret}/savings-rates", Rate("2026-01-01", 1.23456m))))
            .TryGetProperty("annualRate", out _).ShouldBeTrue();
        await admin.CreateAsync($"/api/accounts/{livret}/savings-rates", Rate("2026-01-01", 1.7m));
        (await ErrorsAsync(await admin.PostAsync($"/api/accounts/{livret}/savings-rates", Rate("2026-01-01", 1.5m))))
            .TryGetProperty("effectiveFrom", out _).ShouldBeTrue();
        (await admin.PostAsync($"/api/accounts/{Guid.NewGuid()}/savings-rates", Rate("2026-01-01", 1.7m))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Sans taux, pas d'intérêts affichés sur le compte.
        (await admin.GetJsonAsync($"/api/accounts/{checking}")).GetProperty("interestEarned").ValueKind.ShouldBe(JsonValueKind.Null);

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.PostAsync($"/api/accounts/{livret}/savings-rates", Rate("2026-02-01", 2m))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/accounts/{livret}/interest")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var mine = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Livret Alice",
            type = "Savings",
            initialBalance = 500m,
            initialDate = TestScenario.Iso(Opening),
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        await alice.CreateAsync($"/api/accounts/{mine}/savings-rates", Rate("2026-01-01", 3m));
        (await admin.GetAsync($"/api/accounts/{mine}/savings-rates")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/accounts/{mine}/interest")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/api/accounts/{mine}/savings-rates", Rate("2026-02-01", 2m))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
