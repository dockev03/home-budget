using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class ReimbursementAndAdvanceTests
{
    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> LinkAsync(ApiClient client, Guid transactionId, Guid? reimbursesId, Guid? advanceId) =>
        client.PutAsync($"/api/transactions/{transactionId}/links", new { reimbursesId, advanceId }, null);

    [Fact]
    public async Task A_reimbursement_shows_the_net_cost_of_the_expense()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var health = await admin.CategoryIdAsync("Santé");
        var doctor = await admin.CreateTransactionAsync(account, -50m, health, TestScenario.Today.AddDays(-10), payeeName: "Docteur");
        var cpam = await admin.CreateTransactionAsync(account, 35m, health, TestScenario.Today.AddDays(-3), payeeName: "CPAM");
        var mutual = await admin.CreateTransactionAsync(account, 10m, health, TestScenario.Today.AddDays(-1), payeeName: "Mutuelle");

        var candidates = (await admin.GetJsonAsync($"/api/transactions/{Id(cpam)}/reimbursable?search=docteur")).EnumerateArray().ToList();
        candidates.ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(Id(doctor));

        (await LinkAsync(admin, Id(cpam), Id(doctor), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LinkAsync(admin, Id(mutual), Id(doctor), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var expense = await admin.GetJsonAsync($"/api/transactions/{Id(doctor)}");
        expense.Decimal("reimbursedAmount").ShouldBe(45m);
        (await admin.GetJsonAsync($"/api/transactions/{Id(cpam)}")).GetProperty("reimbursesId").GetGuid().ShouldBe(Id(doctor));

        // Supprimer la dépense défait le lien sans toucher au remboursement.
        (await admin.DeleteAsync($"/api/transactions/{Id(doctor)}", expense.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync($"/api/transactions/{Id(cpam)}")).GetProperty("reimbursesId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invalid_links_are_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var expense = await admin.CreateTransactionAsync(account, -50m);
        var other = await admin.CreateTransactionAsync(account, -20m);
        var income = await admin.CreateTransactionAsync(account, 20m);
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        // Une dépense ne rembourse rien ; un revenu ne rembourse qu'une dépense ; références inconnues.
        (await LinkAsync(admin, Id(other), Id(expense), null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LinkAsync(admin, Id(income), Id(income), null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LinkAsync(admin, Id(income), Guid.NewGuid(), null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LinkAsync(admin, Id(income), null, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LinkAsync(reader, Id(income), Id(expense), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // Défaire un lien inexistant est toujours possible, même sur une dépense.
        (await LinkAsync(admin, Id(other), null, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Links_remain_editable_on_a_reconciled_income()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 0m, initialDate: TestScenario.Today.AddDays(-30));
        var expense = await admin.CreateTransactionAsync(account, -80m, null, TestScenario.Today.AddDays(-5));
        var refund = await admin.CreateTransactionAsync(account, 80m, null, TestScenario.Today.AddDays(-2));
        await admin.CreateAsync($"/api/transactions/{Id(refund)}/clear", new { isCleared = true });
        await admin.CreateAsync($"/api/transactions/{Id(expense)}/clear", new { isCleared = true });
        await admin.CreateAsync($"/api/accounts/{account}/reconciliations", new { date = TestScenario.Iso(TestScenario.Today), statementBalance = 0m });

        var response = await LinkAsync(admin, Id(refund), Id(expense), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiClient.ReadJsonAsync(response)).GetProperty("isReconciled").GetBoolean().ShouldBeTrue();
        (await admin.GetJsonAsync($"/api/transactions/{Id(expense)}")).Decimal("reimbursedAmount").ShouldBe(80m);
    }

    [Fact]
    public async Task An_advance_is_settled_when_the_received_amounts_reach_the_expected_amount()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var advance = await admin.CreateAsync("/api/advances", new
        {
            label = "Note de frais salon",
            expectedAmount = 300m,
            date = TestScenario.Iso(TestScenario.Today.AddDays(-20)),
            notes = (string?)null,
            isClosed = false,
        });
        advance.GetProperty("status").GetString().ShouldBe("Open");
        advance.Decimal("remaining").ShouldBe(300m);

        var first = await admin.CreateTransactionAsync(account, 200m, payeeName: "Employeur");
        await LinkAsync(admin, Id(first), null, Id(advance));
        var open = (await admin.GetJsonAsync("/api/advances"))[0];
        open.Decimal("received").ShouldBe(200m);
        open.Decimal("remaining").ShouldBe(100m);
        open.GetProperty("status").GetString().ShouldBe("Open");

        var second = await admin.CreateTransactionAsync(account, 100m, payeeName: "Employeur");
        await LinkAsync(admin, Id(second), null, Id(advance));
        var settled = (await admin.GetJsonAsync("/api/advances"))[0];
        settled.GetProperty("status").GetString().ShouldBe("Settled");
        settled.Decimal("remaining").ShouldBe(0m);
        settled.GetProperty("transactionCount").GetInt32().ShouldBe(2);

        // Suppression : les revenus restent, sans lien.
        (await admin.DeleteAsync($"/api/advances/{Id(advance)}", settled.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync($"/api/transactions/{Id(first)}")).GetProperty("advanceId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_advance_can_be_closed_by_hand_and_is_validated()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var body = new { label = "Avance CSE", expectedAmount = 50m, date = TestScenario.Iso(TestScenario.Today), notes = "Voyage", isClosed = false };
        var advance = await admin.CreateAsync("/api/advances", body);

        var response = await admin.PutAsync($"/api/advances/{Id(advance)}", body with { isClosed = true }, advance.Version());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var closed = await ApiClient.ReadJsonAsync(response);
        closed.GetProperty("status").GetString().ShouldBe("Settled");
        closed.Decimal("remaining").ShouldBe(0m);
        (await admin.PostAsync("/api/advances", body with { expectedAmount = 0m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/advances", body with { label = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/advances", body with { expectedAmount = 10.123m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
