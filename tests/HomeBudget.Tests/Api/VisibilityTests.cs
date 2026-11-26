using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>R8 : un compte privé n'est visible que de son propriétaire, partout.</summary>
public sealed class VisibilityTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    private static object Account(string name, decimal initialBalance, bool? isPrivate) => new
    {
        name,
        type = "Checking",
        initialBalance,
        initialDate = TestScenario.Iso(Start),
        isPrivate,
    };

    private sealed record Household(ApiClient Admin, ApiClient Alice, ApiClient Bob, Guid Shared, Guid Private, Guid Groceries);

    /// <summary>Compte partagé de l'administrateur ; compte privé d'Alice, avec une dépense, une récurrence et un virement.</summary>
    private static async Task<Household> CreateHouseholdAsync(HomeBudgetFactory factory)
    {
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var bob = await factory.CreateUserClientAsync("bob", "Member");
        var shared = Id(await admin.CreateAsync("/api/accounts", Account("Commun", 1000m, null)));
        var mine = await alice.CreateAsync("/api/accounts", Account("Perso Alice", 500m, true));
        mine.GetProperty("isPrivate").GetBoolean().ShouldBeTrue();
        var groceries = await admin.CategoryIdAsync("Courses");

        await admin.CreateTransactionAsync(shared, -10m, groceries, new DateOnly(2026, 3, 2), payeeName: "Marché");
        await alice.CreateTransactionAsync(Id(mine), -42m, groceries, new DateOnly(2026, 3, 3), payeeName: "Boutique secrète");
        await alice.CreateAsync("/api/transfers", new
        {
            fromAccountId = shared,
            toAccountId = Id(mine),
            date = TestScenario.Iso(new DateOnly(2026, 3, 4)),
            amount = 100m,
            label = "Argent de poche",
        });
        await alice.CreateAsync("/api/recurrences", new
        {
            label = "Abonnement secret",
            accountId = Id(mine),
            payeeName = "Abonnement secret",
            amount = -9.99m,
            mode = "Automatic",
            startDate = TestScenario.Iso(Today.AddDays(5)),
            unit = "Month",
            interval = 1,
            end = "Never",
            isInstallment = false,
            isSubscription = true,
        });
        return new Household(admin, alice, bob, shared, Id(mine), groceries);
    }

    [Fact]
    public async Task A_private_account_and_its_data_are_invisible_to_the_other_members()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await CreateHouseholdAsync(factory);

        foreach (var other in new[] { h.Admin, h.Bob })
        {
            var accounts = await other.GetJsonAsync("/api/accounts");
            accounts.GetProperty("items").EnumerateArray().Select(Id).ShouldBe([h.Shared]);
            accounts.Decimal("netWorth").ShouldBe(890m); // 1 000 − 10 − 100 (sortie du virement)
            (await other.GetAsync($"/api/accounts/{h.Private}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var transactions = (await other.GetJsonAsync("/api/transactions")).GetProperty("items").EnumerateArray().ToList();
            transactions.Count.ShouldBe(2);
            transactions.ShouldAllBe(t => t.GetProperty("accountId").GetGuid() == h.Shared);
            // La sortie du virement reste visible, sans révéler le compte de destination.
            transactions.Single(t => t.GetProperty("transferId").ValueKind != JsonValueKind.Null)
                .GetProperty("transferAccountId").ValueKind.ShouldBe(JsonValueKind.Null);
            (await other.GetJsonAsync("/api/transactions?search=secr")).GetProperty("items").GetArrayLength().ShouldBe(0);

            var courses = (await other.GetJsonAsync("/api/dashboard/2026-03")).GetProperty("expensesByCategory").EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == "Courses");
            courses.Decimal("amount").ShouldBe(10m);

            (await other.GetJsonAsync("/api/recurrences")).GetArrayLength().ShouldBe(0);
            (await other.GetJsonAsync("/api/forecast")).GetProperty("accountIds").EnumerateArray().Select(a => a.GetGuid()).ShouldBe([h.Shared]);
            (await other.GetAsync($"/api/accounts/{h.Private}/reconciliations")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var csv = await (await other.GetAsync("/api/exports/transactions?format=csv")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            csv.ShouldNotContain("secrète");
            csv.ShouldContain("Marché");
        }

        // Le propriétaire voit tout.
        (await h.Alice.GetJsonAsync("/api/accounts")).GetProperty("items").GetArrayLength().ShouldBe(2);
        (await h.Alice.GetJsonAsync("/api/transactions")).GetProperty("items").GetArrayLength().ShouldBe(4);
        (await h.Alice.GetJsonAsync("/api/recurrences")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Other_members_cannot_write_to_a_private_account()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await CreateHouseholdAsync(factory);
        var secret = (await h.Alice.GetJsonAsync("/api/transactions?search=secr")).GetProperty("items")[0];
        var transfer = (await h.Alice.GetJsonAsync("/api/transactions")).GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("transferId").ValueKind != JsonValueKind.Null).GetProperty("transferId").GetGuid();

        (await h.Admin.GetAsync($"/api/transactions/{Id(secret)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await h.Admin.DeleteAsync($"/api/transactions/{Id(secret)}", secret.Version())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await h.Admin.GetAsync($"/api/transfers/{transfer}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await h.Bob.PostAsync("/api/transactions", new
        {
            accountId = h.Private,
            date = TestScenario.Iso(Today),
            payeeName = "Intrus",
            lines = new[] { new { categoryId = (Guid?)null, amount = -1m, memo = (string?)null } },
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await h.Bob.PostAsync("/api/transfers", new
        {
            fromAccountId = h.Private,
            toAccountId = h.Shared,
            date = TestScenario.Iso(Today),
            amount = 50m,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_full_backup_still_contains_every_account()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await CreateHouseholdAsync(factory);

        var backup = await h.Admin.GetJsonAsync("/api/backups/export");

        backup.GetProperty("accounts").GetArrayLength().ShouldBe(2);
        backup.GetProperty("transactions").GetArrayLength().ShouldBe(4);
        backup.GetProperty("recurrences").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Integrity_checks_count_the_data_of_private_accounts()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await CreateHouseholdAsync(factory);
        var category = await h.Admin.CreateAsync("/api/categories", new { name = "Cadeaux secrets", kind = "Variable" });
        await h.Alice.CreateTransactionAsync(h.Private, -30m, Id(category), Today);

        // La catégorie n'est utilisée que dans le compte privé : la supprimer reste refusé.
        var response = await h.Admin.DeleteAsync($"/api/categories/{Id(category)}", category.Version());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_the_author_or_an_admin_can_make_a_shared_account_private()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var bob = await factory.CreateUserClientAsync("bob", "Member");
        var joint = await alice.CreateAsync("/api/accounts", Account("Joint", 0m, null));

        (await bob.PutAsync($"/api/accounts/{Id(joint)}", Account("Joint", 0m, true), joint.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var response = await alice.PutAsync($"/api/accounts/{Id(joint)}", Account("Joint", 0m, true), joint.Version());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ApiClient.ReadJsonAsync(response);
        updated.GetProperty("isPrivate").GetBoolean().ShouldBeTrue();
        (await bob.GetAsync($"/api/accounts/{Id(joint)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Repartagé par sa propriétaire, il redevient visible.
        (await alice.PutAsync($"/api/accounts/{Id(joint)}", Account("Joint", 0m, false), updated.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/accounts/{Id(joint)}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var audit = await admin.GetJsonAsync("/api/audit?action=AccountVisibilityChanged");
        audit.GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Reports_flows_and_calendar_leave_out_the_private_accounts_of_other_members()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await CreateHouseholdAsync(factory);

        foreach (var other in new[] { h.Admin, h.Bob })
        {
            var report = await other.GetJsonAsync("/api/reports/custom?range=CurrentPeriod&groupBy=Payee&includeTransfers=true");
            report.GetProperty("rows").EnumerateArray().ShouldNotContain(r => r.GetProperty("label").GetString() == "Boutique secrète");
            report.Decimal("debit").ShouldBe(110m); // 10 de courses et 100 de virement, côté compte partagé
            var flows = await other.GetJsonAsync("/api/reports/flows?from=2026-03");
            flows.Decimal("outflows").ShouldBe(10m);
            (await other.GetJsonAsync("/api/calendar/2026-03")).GetProperty("entries").GetArrayLength().ShouldBe(0);
        }
        var mine = await h.Alice.GetJsonAsync("/api/reports/custom?range=CurrentPeriod&groupBy=Payee");
        mine.GetProperty("rows").EnumerateArray().ShouldContain(r => r.GetProperty("label").GetString() == "Boutique secrète");
        (await h.Alice.GetJsonAsync("/api/calendar/2026-03")).GetProperty("entries").EnumerateArray()
            .ShouldContain(e => e.GetProperty("label").GetString() == "Abonnement secret");
    }
}
