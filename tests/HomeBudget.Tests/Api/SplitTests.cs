using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Répartition des dépenses communes (K4), sur les seuls comptes partagés (R8).</summary>
public sealed class SplitTests
{
    private static readonly DateOnly Today = new(2026, 3, 20);
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private sealed record Household(ApiClient Admin, ApiClient Alice, Guid AdminId, Guid AliceId);

    private static async Task<Guid> AccountAsync(ApiClient client, string name, Guid? holderId = null, bool isPrivate = false) =>
        (await client.CreateAsync("/api/accounts", new
        {
            name,
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(Opening),
            holderId,
            isPrivate,
        })).GetProperty("id").GetGuid();

    /// <summary>
    /// Compte joint ; courants partagés de l'administrateur et d'Alice (titulaires) ; compte privé d'Alice.
    /// Dépenses communes : 300 (joint) + 100 (admin) + 40 (Alice) ; 50 de cinéma exclu. Apports au joint : 150 (admin), 200 (Alice, depuis son compte privé).
    /// </summary>
    private static async Task<Household> SeedAsync(HomeBudgetFactory factory)
    {
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var adminId = (await admin.GetJsonAsync("/api/auth/me")).GetProperty("id").GetGuid();
        var aliceId = (await alice.GetJsonAsync("/api/auth/me")).GetProperty("id").GetGuid();

        var joint = await AccountAsync(admin, "Joint");
        var adminAccount = await AccountAsync(admin, "Courant admin", adminId);
        var aliceAccount = await AccountAsync(alice, "Courant Alice", aliceId);
        var secret = await AccountAsync(alice, "Perso Alice", aliceId, isPrivate: true);
        (await alice.GetJsonAsync($"/api/accounts/{secret}")).GetProperty("holderId").ValueKind.ShouldBe(JsonValueKind.Null);

        var groceries = await admin.CategoryIdAsync("Courses");
        var restaurants = await admin.CategoryIdAsync("Restaurants");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var cinema = (await admin.CreateAsync("/api/categories", new { name = "Cinéma", kind = "Variable", excludeFromSplit = true }))
            .GetProperty("id").GetGuid();
        var day = new DateOnly(2026, 3, 5);

        await admin.CreateTransactionAsync(joint, -300m, groceries, day);
        await admin.CreateTransactionAsync(adminAccount, -100m, groceries, day);
        await admin.CreateTransactionAsync(adminAccount, -50m, cinema, day);
        await admin.CreateTransactionAsync(adminAccount, 3000m, salary, day);
        await alice.CreateTransactionAsync(aliceAccount, -40m, restaurants, day);
        await alice.CreateTransactionAsync(aliceAccount, 1000m, salary, day);
        await alice.CreateTransactionAsync(secret, -999m, groceries, day);
        await alice.CreateTransactionAsync(secret, 5000m, salary, day);
        await alice.CreateAsync("/api/transfers", new { fromAccountId = secret, toAccountId = joint, date = "2026-03-06", amount = 200m });
        await admin.CreateAsync("/api/transfers", new { fromAccountId = adminAccount, toAccountId = joint, date = "2026-03-06", amount = 150m });
        // Hors période : ignoré.
        await admin.CreateTransactionAsync(joint, -1000m, groceries, new DateOnly(2026, 2, 5));
        return new Household(admin, alice, adminId, aliceId);
    }

    private static Dictionary<Guid, JsonElement> Members(JsonElement split) =>
        split.GetProperty("members").EnumerateArray().ToDictionary(m => m.GetProperty("userId").GetGuid());

    private static async Task SetModeAsync(ApiClient admin, string mode, object[]? shares = null)
    {
        var version = (await admin.GetJsonAsync("/api/split/settings")).Version();
        (await admin.PutAsync("/api/split/settings", new { mode, shares }, version)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Equal_split_counts_expenses_paid_and_contributions_to_joint_accounts_the_same_for_everyone()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await SeedAsync(factory);

        var split = await h.Admin.GetJsonAsync("/api/split/2026-03");
        split.GetProperty("mode").GetString().ShouldBe("Equal");
        split.Decimal("commonExpenses").ShouldBe(440m);
        split.Decimal("jointExpenses").ShouldBe(300m);
        split.Decimal("totalPaid").ShouldBe(490m);
        var members = Members(split);
        var admin = members[h.AdminId];
        (admin.Decimal("expensesPaid"), admin.Decimal("contributions"), admin.Decimal("paid"), admin.Decimal("due"), admin.Decimal("balance"))
            .ShouldBe((100m, 150m, 250m, 245m, 5m));
        var alice = members[h.AliceId];
        (alice.Decimal("expensesPaid"), alice.Decimal("contributions"), alice.Decimal("paid"), alice.Decimal("balance"))
            .ShouldBe((40m, 200m, 240m, -5m));
        // Revenus des comptes partagés seulement : le salaire versé sur le compte privé n'apparaît pas (R8).
        alice.Decimal("income").ShouldBe(1000m);
        var settlement = split.GetProperty("settlements").EnumerateArray().ShouldHaveSingleItem();
        settlement.GetProperty("fromUserId").GetGuid().ShouldBe(h.AliceId);
        settlement.GetProperty("toUserId").GetGuid().ShouldBe(h.AdminId);
        settlement.Decimal("amount").ShouldBe(5m);

        // Alice voit exactement le même résultat : ses comptes privés ne changent rien.
        (await h.Alice.GetJsonAsync("/api/split/2026-03")).GetRawText().ShouldBe(split.GetRawText());
        (await h.Admin.GetAsync("/api/split/2026-13")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Income_pro_rata_and_fixed_shares_change_what_each_member_owes()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await SeedAsync(factory);

        await SetModeAsync(h.Admin, "IncomeProRata");
        var prorata = Members(await h.Admin.GetJsonAsync("/api/split/2026-03"));
        (prorata[h.AdminId].Decimal("share"), prorata[h.AdminId].Decimal("due"), prorata[h.AdminId].Decimal("balance")).ShouldBe((75m, 367.5m, -117.5m));
        (prorata[h.AliceId].Decimal("share"), prorata[h.AliceId].Decimal("balance")).ShouldBe((25m, 117.5m));

        // Sans revenu sur la période, le prorata revient aux parts égales.
        var april = await h.Admin.GetJsonAsync("/api/split/2026-04");
        april.GetProperty("appliedMode").GetString().ShouldBe("Equal");

        await SetModeAsync(h.Admin, "FixedShares", [new { userId = h.AdminId, percent = 60m }, new { userId = h.AliceId, percent = 40m }]);
        var settings = await h.Alice.GetJsonAsync("/api/split/settings");
        settings.GetProperty("shares").EnumerateArray().Single(s => s.GetProperty("userId").GetGuid() == h.AliceId).Decimal("percent").ShouldBe(40m);
        var fixedShares = Members(await h.Admin.GetJsonAsync("/api/split/2026-03"));
        (fixedShares[h.AdminId].Decimal("due"), fixedShares[h.AdminId].Decimal("balance")).ShouldBe((294m, -44m));
        fixedShares[h.AliceId].Decimal("balance").ShouldBe(44m);
    }

    [Fact]
    public async Task An_excluded_member_has_no_share_and_their_payments_count_for_nobody()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await SeedAsync(factory);

        await SetModeAsync(h.Admin, "Equal", [new { userId = h.AdminId, percent = 0m, excluded = true }]);
        var settings = await h.Alice.GetJsonAsync("/api/split/settings");
        var shares = settings.GetProperty("shares").EnumerateArray().ToDictionary(s => s.GetProperty("userId").GetGuid());
        shares[h.AdminId].GetProperty("excluded").GetBoolean().ShouldBeTrue();
        shares[h.AliceId].GetProperty("excluded").GetBoolean().ShouldBeFalse();

        // Alice porte seule la répartition : ses 240 d'apport sont sa part, les 250 de l'administrateur ne comptent pour personne.
        var split = await h.Admin.GetJsonAsync("/api/split/2026-03");
        split.Decimal("commonExpenses").ShouldBe(440m);
        split.Decimal("totalPaid").ShouldBe(240m);
        var alice = split.GetProperty("members").EnumerateArray().ShouldHaveSingleItem();
        alice.GetProperty("userId").GetGuid().ShouldBe(h.AliceId);
        (alice.Decimal("share"), alice.Decimal("paid"), alice.Decimal("balance")).ShouldBe((100m, 240m, 0m));
        split.GetProperty("settlements").EnumerateArray().ShouldBeEmpty();

        // Parts fixes : 100 % entre les seuls membres qui participent.
        await SetModeAsync(h.Admin, "FixedShares", [new { userId = h.AdminId, percent = 0m, excluded = true }, new { userId = h.AliceId, percent = 100m, excluded = false }]);
        Members(await h.Admin.GetJsonAsync("/api/split/2026-03")).Keys.ShouldBe([h.AliceId]);

        // Réintégré : retour au partage à deux.
        await SetModeAsync(h.Admin, "Equal");
        Members(await h.Admin.GetJsonAsync("/api/split/2026-03")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Split_settings_and_holders_are_validated_and_reserved_to_administrators()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var h = await SeedAsync(factory);
        var version = (await h.Admin.GetJsonAsync("/api/split/settings")).Version();

        static async Task<HttpStatusCode> PutAsync(ApiClient client, object body, int? v) => (await client.PutAsync("/api/split/settings", body, v)).StatusCode;
        (await PutAsync(h.Admin, new { mode = "FixedShares", shares = new[] { new { userId = h.AdminId, percent = 60m } } }, version))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(h.Admin, new { mode = "FixedShares", shares = new[] { new { userId = Guid.NewGuid(), percent = 100m } } }, version))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(h.Admin, new { mode = "Equal", shares = new[] { new { userId = h.AdminId, percent = 33.333m } } }, version))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(h.Admin, new { mode = "FixedShares", shares = new[] { new { userId = h.AdminId, percent = 100m, excluded = true } } }, version))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(h.Admin, new
        {
            mode = "Equal",
            shares = new[] { new { userId = h.AdminId, percent = 0m, excluded = true }, new { userId = h.AliceId, percent = 0m, excluded = true } },
        }, version)).ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(h.Admin, new { mode = "Equal" }, null)).ShouldBe(HttpStatusCode.PreconditionRequired);
        (await PutAsync(h.Alice, new { mode = "Equal" }, version)).ShouldBe(HttpStatusCode.Forbidden);

        var response = await h.Admin.PostAsync("/api/accounts", new
        {
            name = "Inconnu",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(Opening),
            holderId = Guid.NewGuid(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("holderId", out _).ShouldBeTrue();
    }
}
