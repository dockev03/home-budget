using System.Net;
using System.Text;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Suivis du foyer : aides CAF et CMG (P1), frais de garde (P2), santé (P4).</summary>
public sealed class FamilyTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static object Recurrence(Guid accountId, Guid categoryId, string label, decimal amount) => new
    {
        label,
        accountId,
        categoryId,
        amount,
        mode = "ToValidate",
        startDate = TestScenario.Iso(Today),
        unit = "Month",
        interval = 1,
        end = "Never",
        isInstallment = false,
    };

    private static Guid Pending(JsonElement pending, string label) =>
        Id(pending.EnumerateArray().First(p => p.GetProperty("label").GetString() == label));

    [Fact]
    public async Task Benefits_compare_the_expected_amount_with_what_was_received()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var caf = await admin.CategoryIdAsync("Allocation CAF");
        var cmg = await admin.CategoryIdAsync("CMG");

        var cafRecurrence = await admin.CreateAsync("/api/recurrences", Recurrence(account, caf, "Allocations", 120m));
        await admin.CreateAsync("/api/recurrences", Recurrence(account, cmg, "CMG", 300m));
        var pending = await admin.GetJsonAsync("/api/recurrences/pending");
        (await admin.PostAsync($"/api/recurrences/occurrences/{Pending(pending, "Allocations")}/confirm", new { amount = 115m }))
            .EnsureSuccessStatusCode();
        // Versement ponctuel, hors échéancier.
        await admin.CreateTransactionAsync(account, 50m, caf, new DateOnly(2026, 2, 3), label: "Rappel CAF");
        // Compte privé d'Alice : invisible pour l'administrateur (R8).
        var secret = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso Alice",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(Opening),
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        await alice.CreateTransactionAsync(secret, 999m, caf, new DateOnly(2026, 2, 3));

        // L'attendu est figé : relever la récurrence ne change que l'à venir.
        var current = await admin.GetJsonAsync($"/api/recurrences/{Id(cafRecurrence)}");
        (await admin.PutAsync($"/api/recurrences/{Id(cafRecurrence)}", Recurrence(account, caf, "Allocations", 130m), current.Version()))
            .EnsureSuccessStatusCode();

        var benefits = await admin.GetJsonAsync("/api/family/benefits/2026");
        var rows = benefits.GetProperty("rows").EnumerateArray().ToList();
        var received = rows.Single(r => r.GetProperty("status").GetString() == "Received");
        received.Decimal("expected").ShouldBe(120m);
        received.Decimal("received").ShouldBe(115m);
        received.Decimal("gap").ShouldBe(-5m);
        var waiting = rows.Single(r => r.GetProperty("status").GetString() == "Pending");
        waiting.GetProperty("categoryName").GetString().ShouldBe("CMG");
        waiting.GetProperty("gap").ValueKind.ShouldBe(JsonValueKind.Null);
        var unscheduled = rows.Single(r => r.GetProperty("status").GetString() == "Unscheduled");
        unscheduled.Decimal("received").ShouldBe(50m);
        unscheduled.GetProperty("label").GetString().ShouldBe("Rappel CAF");
        rows.Count(r => r.GetProperty("status").GetString() == "Upcoming").ShouldBe(18); // avril à décembre, deux aides

        var cafTotals = benefits.GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("categoryId").GetGuid() == caf);
        cafTotals.Decimal("expected").ShouldBe(120m);
        cafTotals.Decimal("received").ShouldBe(165m);
        cafTotals.Decimal("gap").ShouldBe(45m);
        cafTotals.Decimal("upcoming").ShouldBe(9 * 130m);
        var total = benefits.GetProperty("total");
        total.Decimal("expected").ShouldBe(420m);
        total.Decimal("upcoming").ShouldBe(9 * 430m);

        // Alice voit son versement privé, hors échéancier.
        var aliceRows = (await alice.GetJsonAsync("/api/family/benefits/2026")).GetProperty("rows").EnumerateArray();
        aliceRows.Count(r => r.GetProperty("status").GetString() == "Unscheduled").ShouldBe(2);

        (await admin.GetAsync("/api/family/benefits/1800")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Childcare_summary_deducts_childcare_aid_for_the_tax_return()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2025, 1, 1));
        var nanny = await admin.CategoryIdAsync("Nounou Maël");
        var cmg = await admin.CategoryIdAsync("CMG");
        // Une sous-catégorie sans rôle prend celui de son parent.
        var canteen = await admin.CreateCategoryAsync("Goûters nounou", "Fixed", nanny);

        await admin.CreateTransactionAsync(account, -500m, nanny, new DateOnly(2026, 1, 31), payeeName: "Nounou");
        await admin.CreateTransactionAsync(account, -500m, nanny, new DateOnly(2026, 2, 28), payeeName: "Nounou");
        await admin.CreateTransactionAsync(account, 20m, nanny, new DateOnly(2026, 2, 28), payeeName: "Nounou");
        await admin.CreateTransactionAsync(account, -100m, canteen, new DateOnly(2026, 3, 2));
        await admin.CreateTransactionAsync(account, 300m, cmg, new DateOnly(2026, 2, 10));
        await admin.CreateTransactionAsync(account, -999m, nanny, new DateOnly(2025, 12, 31)); // autre année

        var summary = await admin.GetJsonAsync("/api/family/childcare/2026");
        summary.Decimal("paid").ShouldBe(1080m);
        summary.Decimal("aid").ShouldBe(300m);
        summary.Decimal("declarable").ShouldBe(780m);
        var months = summary.GetProperty("months").EnumerateArray().ToList();
        months.Count.ShouldBe(12);
        months[1].Decimal("paid").ShouldBe(480m);
        months[1].Decimal("aid").ShouldBe(300m);
        months[1].Decimal("net").ShouldBe(180m);
        var payees = summary.GetProperty("payees").EnumerateArray().ToList();
        payees[0].GetProperty("name").GetString().ShouldBe("Nounou");
        payees[0].Decimal("paid").ShouldBe(980m);
        payees[1].GetProperty("name").GetString().ShouldBe("Sans tiers");

        var export = await admin.GetAsync("/api/family/childcare/2026/export");
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        export.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("frais-de-garde-2026.csv");
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        csv.ShouldContain("février;480,00;300,00;180,00");
        csv.ShouldContain("Montant déclarable;780,00");
        var audit = await admin.GetJsonAsync("/api/audit?action=Exported");
        audit.GetProperty("items").EnumerateArray().ShouldContain(e => e.GetProperty("details").GetString()!.Contains("Frais de garde 2026"));
    }

    [Fact]
    public async Task Health_expenses_show_primary_and_mutual_reimbursements_and_what_remains()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var health = await admin.CategoryIdAsync("Santé");
        var doctor = await admin.CreateTransactionAsync(account, -50m, health, new DateOnly(2026, 2, 1), payeeName: "Docteur");
        var cpam = await admin.CreateTransactionAsync(account, 35m, health, new DateOnly(2026, 2, 5), payeeName: "CPAM");
        var mutual = await admin.CreateTransactionAsync(account, 10m, null, new DateOnly(2026, 2, 8), payeeName: "Mutuelle");
        await admin.CreateTransactionAsync(account, -20m, health, new DateOnly(2026, 3, 1), payeeName: "Pharmacie");
        await admin.CreateTransactionAsync(account, 5m, health, new DateOnly(2026, 3, 2), payeeName: "CPAM");
        foreach (var refund in new[] { cpam, mutual })
        {
            (await admin.PutAsync($"/api/transactions/{Id(refund)}/links", new { reimbursesId = Id(doctor), advanceId = (Guid?)null }, null))
                .EnsureSuccessStatusCode();
        }

        // Sans organisme désigné, les remboursements sont « autres ».
        var before = (await admin.GetJsonAsync("/api/family/health?from=2026-01-01&to=2026-12-31")).GetProperty("rows").EnumerateArray().ToList();
        before.Single(r => r.GetProperty("payee").GetString() == "Docteur").Decimal("other").ShouldBe(45m);

        (await admin.PutAsync($"/api/payees/{cpam.GetProperty("payeeId").GetGuid()}/reimburser", new { reimburser = "PrimaryInsurance" }, null))
            .EnsureSuccessStatusCode();
        (await admin.PutAsync($"/api/payees/{mutual.GetProperty("payeeId").GetGuid()}/reimburser", new { reimburser = "Mutual" }, null))
            .EnsureSuccessStatusCode();
        var organisms = (await admin.GetJsonAsync("/api/payees?reimbursers=true")).EnumerateArray().ToList();
        organisms.Select(p => p.GetProperty("name").GetString()).Order().ShouldBe(["CPAM", "Mutuelle"]);

        var report = await admin.GetJsonAsync("/api/family/health"); // défaut : l'année en cours
        report.GetProperty("from").GetString().ShouldBe("2026-01-01");
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        var visit = rows.Single(r => r.GetProperty("payee").GetString() == "Docteur");
        visit.Decimal("expense").ShouldBe(50m);
        visit.Decimal("primaryInsurance").ShouldBe(35m);
        visit.Decimal("mutual").ShouldBe(10m);
        visit.Decimal("other").ShouldBe(0m);
        visit.Decimal("remaining").ShouldBe(5m);
        rows.Single(r => r.GetProperty("payee").GetString() == "Pharmacie").Decimal("remaining").ShouldBe(20m);
        report.Decimal("expense").ShouldBe(70m);
        report.Decimal("remaining").ShouldBe(25m);
        report.Decimal("unlinked").ShouldBe(5m);

        (await admin.GetAsync("/api/family/health?from=2026-03-01&to=2026-02-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/payees/{Guid.NewGuid()}/reimburser", new { reimburser = "Mutual" }, null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var viewer = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await viewer.PutAsync($"/api/payees/{cpam.GetProperty("payeeId").GetGuid()}/reimburser", new { reimburser = "None" }, null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Family_roles_follow_the_category_kind()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var categories = (await admin.GetJsonAsync("/api/categories")).EnumerateArray().ToList();
        string Role(string name) => categories.Single(c => c.GetProperty("name").GetString() == name).GetProperty("familyRole").GetString()!;
        Role("Allocation CAF").ShouldBe("Benefit");
        Role("CMG").ShouldBe("ChildcareAid");
        Role("Nounou Maël").ShouldBe("Childcare");
        Role("Santé").ShouldBe("Health");
        Role("Courses").ShouldBe("None");

        (await admin.PostAsync("/api/categories", new { name = "Mauvaise", kind = "Variable", familyRole = "Benefit" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var dentist = await admin.CreateAsync("/api/categories", new { name = "Dentiste", kind = "Variable", familyRole = "Health" });
        dentist.GetProperty("familyRole").GetString().ShouldBe("Health");

        // Devenue un revenu, la catégorie perd un rôle de dépense.
        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/categories/{Id(dentist)}",
            new { name = "Dentiste", kind = "Income" }, dentist.Version()));
        updated.GetProperty("familyRole").GetString().ShouldBe("None");
    }
}
