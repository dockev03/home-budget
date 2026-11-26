using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>L4 : rejeu idempotent de la file d'attente hors ligne.</summary>
public sealed class SyncTests
{
    private static object Item(Guid id, Guid accountId, decimal amount, string? payee = "Boulangerie", Guid? categoryId = null) => new
    {
        id,
        transaction = new
        {
            accountId,
            date = TestScenario.Iso(TestScenario.Today),
            payeeName = payee,
            label = (string?)null,
            notes = (string?)null,
            isCleared = (bool?)null,
            lines = new[] { new { categoryId, amount, memo = (string?)null } },
        },
    };

    private static List<JsonElement> Items(JsonElement response) => [.. response.GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Rejouer_la_file_ne_cree_pas_de_doublon()
    {
        using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var groceries = await admin.CategoryIdAsync("Courses");
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var sent = Items(await admin.CreateAsync("/api/sync", new { items = new[] { Item(first, account, -4.5m, categoryId: groceries), Item(second, account, -12m) } }));
        sent.Select(i => i.GetProperty("status").GetString()).ShouldBe(["Created", "Created"]);
        sent[0].GetProperty("transaction").GetProperty("id").GetGuid().ShouldBe(first);
        sent[0].GetProperty("transaction").GetProperty("payeeName").GetString().ShouldBe("Boulangerie");

        // Réponse perdue : l'appareil renvoie toute la file.
        var replay = Items(await admin.CreateAsync("/api/sync", new { items = new[] { Item(first, account, -4.5m), Item(second, account, -12m) } }));
        replay.Select(i => i.GetProperty("status").GetString()).ShouldBe(["AlreadyApplied", "AlreadyApplied"]);
        replay[0].GetProperty("transaction").GetProperty("amount").GetDecimal().ShouldBe(-4.5m);

        var list = await admin.GetJsonAsync($"/api/transactions?accountId={account}");
        list.GetProperty("items").GetArrayLength().ShouldBe(2);
        (await admin.GetJsonAsync($"/api/transactions/{first}")).GetProperty("createdBy").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Un_element_refuse_n_empeche_pas_les_suivants()
    {
        using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var archived = await admin.CreateAccountAsync("Ancien");
        var json = await admin.GetJsonAsync($"/api/accounts/{archived}");
        (await admin.PutAsync($"/api/accounts/{archived}", new
        {
            name = "Ancien",
            type = "Checking",
            initialBalance = 0m,
            initialDate = json.GetProperty("initialDate").GetString(),
            isArchived = true,
        }, json.Version())).EnsureSuccessStatusCode();

        var results = Items(await admin.CreateAsync("/api/sync", new
        {
            items = new[]
            {
                Item(Guid.CreateVersion7(), archived, -3m),
                Item(Guid.CreateVersion7(), account, 0m),
                Item(Guid.CreateVersion7(), account, -8m),
            },
        }));

        results.Select(i => i.GetProperty("status").GetString()).ShouldBe(["Rejected", "Rejected", "Created"]);
        results[0].GetProperty("errors").GetProperty("accountId")[0].GetString()!.ShouldContain("archivé");
        results[1].GetProperty("errors").EnumerateObject().Single().Name.ShouldBe("lines[0].amount");
        results[2].GetProperty("errors").ValueKind.ShouldBe(JsonValueKind.Null);

        // Élément mal formé : toute la requête est refusée, rien n'est créé.
        var malformed = await admin.PostAsync("/api/sync", new
        {
            items = new[] { Item(Guid.CreateVersion7(), account, -1m), Item(Guid.CreateVersion7(), account, -7m, payee: new string('x', 101)) },
        });
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetJsonAsync($"/api/transactions?accountId={account}")).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Les_identifiants_doivent_etre_uniques_et_la_saisie_autorisee()
    {
        using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var id = Guid.CreateVersion7();

        (await admin.PostAsync("/api/sync", new { items = new[] { Item(id, account, -1m), Item(id, account, -2m) } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/sync", new { items = new[] { Item(Guid.Empty, account, -1m) } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/sync", new { items = Array.Empty<object>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.PostAsync("/api/sync", new { items = new[] { Item(Guid.CreateVersion7(), account, -1m) } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_identifiant_pris_sur_un_compte_prive_est_refuse_sans_rien_reveler()
    {
        using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var shared = await admin.CreateAccountAsync("Commun");
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var mine = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(TestScenario.Today.AddYears(-1)),
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        var id = Guid.CreateVersion7();
        Items(await alice.CreateAsync("/api/sync", new { items = new[] { Item(id, mine, -5m, payee: "Secret") } }))[0]
            .GetProperty("status").GetString().ShouldBe("Created");

        var result = Items(await admin.CreateAsync("/api/sync", new { items = new[] { Item(id, shared, -5m) } }))[0];

        result.GetProperty("status").GetString().ShouldBe("Rejected");
        result.GetProperty("transaction").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetRawText().ShouldNotContain("Secret");
    }
}
