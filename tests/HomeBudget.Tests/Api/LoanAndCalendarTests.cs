using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Crédits (A8) et calendrier des échéances (E8).</summary>
public sealed class LoanAndCalendarTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static object Terms(decimal principal = 12_000m, decimal rate = 3.875m, int months = 48, string first = "2026-01-05", decimal insurance = 6.5m) =>
        new { principal, annualRate = rate, durationMonths = months, firstPaymentDate = first, monthlyInsurance = insurance };

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        return (await ApiClient.ReadJsonAsync(response)).GetProperty("errors");
    }

    [Fact]
    public async Task Loan_terms_give_the_schedule_and_the_remaining_principal()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var credit = await admin.CreateAccountAsync("Prêt auto", -12_000m, "Credit", Opening);
        (await admin.GetAsync($"/api/accounts/{credit}/loan")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var created = await admin.PutAsync($"/api/accounts/{credit}/loan", Terms(), null);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var loan = await ApiClient.ReadJsonAsync(created);
        loan.Decimal("payment").ShouldBe(270.28m);
        loan.Decimal("paymentWithInsurance").ShouldBe(276.78m);
        loan.Decimal("totalInterest").ShouldBe(973.33m);
        loan.Decimal("totalInsurance").ShouldBe(312m);
        loan.Decimal("totalCost").ShouldBe(1285.33m);
        loan.GetProperty("endDate").GetString().ShouldBe("2029-12-05");
        // Mensualités du 5 janvier, 5 février et 5 mars payées au 10 mars.
        loan.GetProperty("paidCount").GetInt32().ShouldBe(3);
        loan.GetProperty("schedule").GetArrayLength().ShouldBe(48);
        var remaining = loan.Decimal("remainingPrincipal");
        loan.GetProperty("schedule")[2].Decimal("remaining").ShouldBe(remaining);

        var account = await admin.GetJsonAsync($"/api/accounts/{credit}");
        account.Decimal("remainingPrincipal").ShouldBe(remaining);
        (await admin.GetJsonAsync("/api/accounts")).GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("id").GetGuid() == credit).Decimal("remainingPrincipal").ShouldBe(remaining);

        // Une modification exige la version.
        (await admin.PutAsync($"/api/accounts/{credit}/loan", Terms(rate: 2m), null)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/accounts/{credit}/loan", Terms(rate: 0m, months: 3, insurance: 0m), loan.Version()));
        updated.Decimal("payment").ShouldBe(4000m);
        updated.Decimal("totalCost").ShouldBe(0m);
        updated.Decimal("remainingPrincipal").ShouldBe(0m);

        // Tant qu'il a des conditions de crédit, le compte reste un compte de crédit.
        var typeChange = await admin.PutAsync($"/api/accounts/{credit}", new
        {
            name = "Prêt auto",
            type = "Checking",
            initialBalance = -12_000m,
            initialDate = TestScenario.Iso(Opening),
        }, account.Version());
        (await ErrorsAsync(typeChange)).TryGetProperty("type", out _).ShouldBeTrue();

        (await admin.DeleteAsync($"/api/accounts/{credit}/loan", updated.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync($"/api/accounts/{credit}")).GetProperty("remainingPrincipal").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invalid_loan_terms_are_rejected_and_private_loans_stay_private()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var credit = await admin.CreateAccountAsync("Prêt", -1000m, "Credit", Opening);

        (await ErrorsAsync(await admin.PutAsync($"/api/accounts/{checking}/loan", Terms(), null))).TryGetProperty("accountId", out _).ShouldBeTrue();
        var errors = await ErrorsAsync(await admin.PutAsync($"/api/accounts/{credit}/loan", Terms(principal: 0m, rate: 3.12345m, months: 601, insurance: -1m), null));
        foreach (var field in new[] { "principal", "annualRate", "durationMonths", "monthlyInsurance" })
        {
            errors.TryGetProperty(field, out _).ShouldBeTrue(field);
        }
        (await admin.PutAsync($"/api/accounts/{Guid.NewGuid()}/loan", Terms(), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.PutAsync($"/api/accounts/{credit}/loan", Terms(), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var mine = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Crédit Alice",
            type = "Credit",
            initialBalance = -5000m,
            initialDate = TestScenario.Iso(Opening),
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        (await alice.PutAsync($"/api/accounts/{mine}/loan", Terms(principal: 5000m), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/accounts/{mine}/loan")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PutAsync($"/api/accounts/{mine}/loan", Terms(), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static object Recurrence(Guid accountId, string label, decimal amount, string mode, string unit = "Month") => new
    {
        label,
        accountId,
        payeeName = label,
        amount,
        mode,
        startDate = TestScenario.Iso(Today),
        unit,
        interval = 1,
        end = "Never",
        isInstallment = false,
    };

    [Fact]
    public async Task Calendar_lists_the_occurrences_of_the_period_with_their_status()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        await admin.CreateAsync("/api/recurrences", Recurrence(checking, "Loyer", -700m, "Automatic"));
        await admin.CreateAsync("/api/recurrences", Recurrence(checking, "Marché", -20m, "ToValidate", "Week"));
        await admin.CreateAsync("/api/recurrences", Recurrence(checking, "Facture", -60m, "ToValidate"));
        await admin.CreateAsync("/api/recurrences", Recurrence(checking, "Remboursement", 100m, "ToValidate"));

        var pending = (await admin.GetJsonAsync("/api/recurrences/pending")).EnumerateArray()
            .ToDictionary(p => p.GetProperty("label").GetString()!, p => p.GetProperty("id").GetGuid());
        (await admin.PostAsync($"/api/recurrences/occurrences/{pending["Marché"]}/confirm", new { amount = -25m })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/recurrences/occurrences/{pending["Facture"]}/postpone", new { date = "2026-04-02" })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/recurrences/occurrences/{pending["Remboursement"]}/skip")).EnsureSuccessStatusCode();

        var march = await admin.GetJsonAsync("/api/calendar/2026-03");
        var entries = march.GetProperty("entries").EnumerateArray()
            .Select(e => (Label: e.GetProperty("label").GetString(), Date: e.GetProperty("date").GetString(), Status: e.GetProperty("status").GetString(), Amount: e.Decimal("amount")))
            .ToList();
        entries.ShouldBe(
        [
            ("Loyer", "2026-03-10", "Generated", -700m),
            ("Marché", "2026-03-10", "Generated", -25m),
            ("Remboursement", "2026-03-10", "Skipped", 100m),
            ("Marché", "2026-03-17", "Upcoming", -20m),
            ("Marché", "2026-03-24", "Upcoming", -20m),
            ("Marché", "2026-03-31", "Upcoming", -20m),
        ]);
        march.Decimal("income").ShouldBe(0m);
        march.Decimal("expenses").ShouldBe(785m);
        march.GetProperty("entries")[0].GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.String);

        // L'échéance reportée apparaît à sa nouvelle date, toujours à valider.
        var april = (await admin.GetJsonAsync("/api/calendar/2026-04")).GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("label").GetString() == "Facture" && e.GetProperty("status").GetString() == "Pending");
        april.GetProperty("date").GetString().ShouldBe("2026-04-02");
        april.GetProperty("scheduledDate").GetString().ShouldBe("2026-03-10");

        (await admin.GetAsync("/api/calendar/2026-13")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
