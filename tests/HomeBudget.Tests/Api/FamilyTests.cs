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

    /// <summary>Déclaration Pajemploi de la capture de novembre 2026 (salarié = tiers).</summary>
    internal static object Declaration(int year, int month, Guid payeeId, decimal netSalary = 638.25m, decimal cmg = 446.40m) => new
    {
        year,
        month,
        payeeId,
        activityDays = 16,
        normalHours = 142m,
        extraHours = 0m,
        overtimeHours = 0m,
        paidLeaveDays = 0m,
        netHourlyRate = (decimal?)null,
        netSalary,
        maintenanceAllowance = 67.50m,
        mealAllowance = 82.50m,
        mileageAllowance = 0m,
        advance = 0m,
        longCareDays = 0,
        disabledChildCare = false,
        isContractEnd = false,
        contractEndDate = (string?)null,
        precariousnessAllowance = 0m,
        paidLeaveCompensation = 0m,
        noticeCompensation = 0m,
        terminationAllowance = 0m,
        contributions = 539.53m,
        coveredContributions = 539.53m,
        cmgEntitlement = cmg,
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
    public async Task Childcare_summary_counts_each_transaction_in_its_reference_month()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2025, 1, 1));
        var nanny = await admin.CategoryIdAsync("Nounou Maël");
        var cmg = await admin.CategoryIdAsync("CMG");

        object Payment(decimal amount, Guid category, DateOnly date, string? referenceMonth) => new
        {
            accountId = account,
            date = TestScenario.Iso(date),
            referenceMonth,
            lines = new[] { new { categoryId = category, amount, memo = (string?)null } },
        };

        // Garde de novembre : CMG versé le 28/11, salaire payé le 1er décembre.
        await admin.CreateTransactionAsync(account, 400m, cmg, new DateOnly(2026, 11, 28));
        var november = await admin.CreateAsync("/api/transactions", Payment(-600m, nanny, new DateOnly(2026, 12, 1), "2026-11-15"));
        november.GetProperty("referenceMonth").GetString().ShouldBe("2026-11-01");
        // Garde de décembre payée en janvier : elle reste dans l'année de la garde.
        await admin.CreateAsync("/api/transactions", Payment(-650m, nanny, new DateOnly(2027, 1, 2), "2026-12-01"));
        // Un mois concerné égal à celui de la date n'est pas conservé.
        var same = await admin.CreateAsync("/api/transactions", Payment(-10m, nanny, new DateOnly(2026, 10, 5), "2026-10-01"));
        same.GetProperty("referenceMonth").ValueKind.ShouldBe(JsonValueKind.Null);

        var summary = await admin.GetJsonAsync("/api/family/childcare/2026");
        var months = summary.GetProperty("months").EnumerateArray().ToList();
        months[10].Decimal("paid").ShouldBe(600m);
        months[10].Decimal("aid").ShouldBe(400m);
        months[11].Decimal("paid").ShouldBe(650m);
        summary.Decimal("paid").ShouldBe(1260m);
        (await admin.GetJsonAsync("/api/family/childcare/2027")).Decimal("paid").ShouldBe(0m);

        (await admin.PostAsync("/api/transactions", Payment(-1m, nanny, new DateOnly(2026, 12, 1), "2025-11-01")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Sans mois concerné, l'opération revient au mois de sa date.
        var cleared = await ApiClient.ReadJsonAsync(await admin.PutAsync($"/api/transactions/{Id(november)}",
            Payment(-600m, nanny, new DateOnly(2026, 12, 1), null), november.Version()));
        cleared.GetProperty("referenceMonth").ValueKind.ShouldBe(JsonValueKind.Null);
        var after = (await admin.GetJsonAsync("/api/family/childcare/2026")).GetProperty("months").EnumerateArray().ToList();
        after[10].Decimal("paid").ShouldBe(0m);
        after[11].Decimal("paid").ShouldBe(1250m);
    }

    [Fact]
    public async Task Childcare_declarations_are_saved_per_month_and_employee_and_compared_with_payments()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2025, 1, 1));
        var nanny = await admin.CategoryIdAsync("Nounou Maël");
        var cmg = await admin.CategoryIdAsync("CMG");
        await admin.CreateTransactionAsync(account, -800m, nanny, new DateOnly(2026, 11, 30), payeeName: "Anaïs");
        await admin.CreateTransactionAsync(account, -50m, nanny, new DateOnly(2026, 11, 30), payeeName: "Lucie");
        await admin.CreateTransactionAsync(account, 446.40m, cmg, new DateOnly(2026, 11, 28));
        async Task<Guid> PayeeAsync(string name) =>
            (await admin.GetJsonAsync($"/api/payees?search={name}")).EnumerateArray().First().GetProperty("id").GetGuid();
        var anais = await PayeeAsync("Anaïs");
        var lucie = await PayeeAsync("Lucie");

        var created = await admin.CreateAsync("/api/family/childcare/declarations", Declaration(2026, 11, anais));
        created.GetProperty("payeeName").GetString().ShouldBe("Anaïs");
        created.Decimal("remuneration").ShouldBe(788.25m);
        created.Decimal("totalCost").ShouldBe(1327.78m);
        created.Decimal("cmg").ShouldBe(985.93m);
        created.Decimal("remaining").ShouldBe(341.85m);
        // Plusieurs salariés le même mois : une déclaration chacun.
        await admin.CreateAsync("/api/family/childcare/declarations", Declaration(2026, 11, lucie, netSalary: 0m, cmg: 0m));

        // Une seule déclaration par mois et par salarié.
        (await admin.PostAsync("/api/family/childcare/declarations", Declaration(2026, 11, anais)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        // Saisie invalide : mois, montant négatif, salarié inconnu, indemnité de fin hors fin de contrat.
        (await admin.PostAsync("/api/family/childcare/declarations", Declaration(2026, 13, anais)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/family/childcare/declarations", Declaration(2026, 10, anais, netSalary: -1m)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/family/childcare/declarations", Declaration(2026, 10, Guid.NewGuid())))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Lecture seule : consultation, sans écriture.
        (await reader.PostAsync("/api/family/childcare/declarations", Declaration(2026, 10, anais)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var list = (await reader.GetJsonAsync("/api/family/childcare/2026/declarations")).EnumerateArray().ToList();
        list.Select(d => d.GetProperty("payeeName").GetString()).ShouldBe(["Anaïs", "Lucie"]);
        (await admin.GetJsonAsync("/api/family/childcare/2025/declarations")).GetArrayLength().ShouldBe(0);

        // Récapitulatif : écart entre le payé (850) et la rémunération déclarée (788,25 + 150), et sur le CMG.
        var november = (await admin.GetJsonAsync("/api/family/childcare/2026")).GetProperty("months")[10];
        november.GetProperty("declarations").GetInt32().ShouldBe(2);
        november.Decimal("declaredRemuneration").ShouldBe(938.25m);
        november.Decimal("paidGap").ShouldBe(-88.25m);
        november.Decimal("declaredCmg").ShouldBe(446.40m);
        november.Decimal("aidGap").ShouldBe(0m);
        var october = (await admin.GetJsonAsync("/api/family/childcare/2026")).GetProperty("months")[9];
        october.GetProperty("declarations").GetInt32().ShouldBe(0);
        october.GetProperty("paidGap").ValueKind.ShouldBe(JsonValueKind.Null);

        // Modification (If-Match) : version périmée refusée, puis la bonne acceptée.
        var url = $"/api/family/childcare/declarations/{Id(created)}";
        (await admin.PutAsync(url, Declaration(2026, 11, anais, netSalary: 700m), created.Version() + 1))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var updated = await ApiClient.ReadJsonAsync(await admin.PutAsync(url, Declaration(2026, 11, anais, netSalary: 700m), created.Version()));
        updated.Decimal("remuneration").ShouldBe(850m);
        // Déplacer la déclaration de Lucie sur Anaïs : doublon.
        var lucieDeclaration = list[1];
        (await admin.PutAsync($"/api/family/childcare/declarations/{Id(lucieDeclaration)}", Declaration(2026, 11, anais), lucieDeclaration.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await admin.DeleteAsync(url, updated.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync("/api/family/childcare/2026/declarations")).GetArrayLength().ShouldBe(1);
        // La suppression est inscrite au journal (K2) ; créations et modifications portent auteur et version.
        var audit = await admin.GetJsonAsync("/api/audit?action=Deleted");
        audit.GetProperty("items").EnumerateArray().ShouldContain(e => e.GetProperty("target").GetString() == "ChildcareDeclaration");
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
