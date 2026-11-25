using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class RuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static HomeBudgetFactory Factory() => new() { Today = Today };

    /// <summary>Relevé OFX minimal : (date, montant, identifiant, libellé).</summary>
    private static string Ofx(params (DateOnly Date, decimal Amount, string Id, string Name)[] lines)
    {
        var builder = new StringBuilder("<OFX><BANKTRANLIST>");
        foreach (var (date, amount, id, name) in lines)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<STMTTRN><DTPOSTED>{date:yyyyMMdd}<TRNAMT>{amount.ToString(CultureInfo.InvariantCulture)}<FITID>{id}<NAME>{name}</STMTTRN>");
        }
        builder.Append("</BANKTRANLIST></OFX>");
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    private static async Task<JsonElement> PreviewAsync(ApiClient client, Guid accountId, string content) =>
        await client.CreateAsync("/api/imports/preview", new { accountId, fileName = "releve.ofx", content, profileId = (Guid?)null });

    private static async Task ImportAsync(ApiClient client, Guid accountId, string content)
    {
        var preview = await PreviewAsync(client, accountId, content);
        await client.CreateAsync("/api/imports", new
        {
            accountId,
            fileName = "releve.ofx",
            content,
            profileId = (Guid?)null,
            lines = preview.GetProperty("lines").EnumerateArray()
                .Select(l => new { line = l.GetProperty("line").GetInt32(), acceptMatch = false }).ToArray(),
        });
    }

    private static async Task<List<JsonElement>> TransactionsAsync(ApiClient client, Guid accountId) =>
        [.. (await client.GetJsonAsync($"/api/transactions?accountId={accountId}")).GetProperty("items").EnumerateArray()];

    /// <summary>Corrige le tiers et la catégorie d'une opération non ventilée.</summary>
    private static async Task<JsonElement> CorrectAsync(ApiClient client, JsonElement transaction, string payeeName, Guid categoryId)
    {
        var response = await client.PutAsync($"/api/transactions/{transaction.GetProperty("id").GetGuid()}", new
        {
            accountId = transaction.GetProperty("accountId").GetGuid(),
            date = transaction.GetProperty("date").GetString(),
            payeeName,
            label = transaction.GetProperty("label").GetString(),
            lines = new[] { new { categoryId, amount = transaction.Decimal("amount") } },
        }, transaction.Version());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiClient.ReadJsonAsync(response);
    }

    [Fact]
    public async Task Correcting_an_imported_transaction_learns_a_rule_applied_to_the_next_statement()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2026, 1, 1));
        var insurance = await admin.CreateCategoryAsync("Assurances", "Fixed");
        await ImportAsync(admin, account, Ofx((new DateOnly(2026, 10, 25), -45.10m, "A1", "PRLV SEPA MACIF SA ECH/251025 ID EMETTEUR/FR12ZZZ")));

        var imported = (await TransactionsAsync(admin, account)).ShouldHaveSingleItem();
        imported.GetProperty("payeeName").ValueKind.ShouldBe(JsonValueKind.Null);
        await CorrectAsync(admin, imported, "Macif", insurance);

        var rule = (await admin.GetJsonAsync("/api/rules")).EnumerateArray().ShouldHaveSingleItem();
        rule.GetProperty("key").GetString().ShouldBe("MACIF SA");
        rule.GetProperty("payeeName").GetString().ShouldBe("Macif");
        rule.GetProperty("categoryId").GetGuid().ShouldBe(insurance);

        var next = Ofx((new DateOnly(2026, 11, 24), -45.10m, "A2", "PRLV SEPA MACIF SA ECH/251125 ID EMETTEUR/FR12ZZZ"));
        var line = (await PreviewAsync(admin, account, next)).GetProperty("lines")[0];
        line.GetProperty("suggestedPayeeName").GetString().ShouldBe("Macif");
        line.GetProperty("suggestedCategoryId").GetGuid().ShouldBe(insurance);

        await ImportAsync(admin, account, next);

        var created = (await TransactionsAsync(admin, account))[0];
        created.GetProperty("date").GetString().ShouldBe("2026-11-24");
        created.GetProperty("payeeName").GetString().ShouldBe("Macif");
        created.GetProperty("lines")[0].GetProperty("categoryId").GetGuid().ShouldBe(insurance);
    }

    [Fact]
    public async Task Suggestion_gives_the_usual_category_of_the_payee()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var groceries = await admin.CategoryIdAsync("Courses");
        var dog = await admin.CategoryIdAsync("Chien");
        await admin.CreateTransactionAsync(account, -50m, groceries, Today.AddDays(-10), payeeName: "Leclerc");
        await admin.CreateTransactionAsync(account, -60m, groceries, Today.AddDays(-5), payeeName: "Leclerc");
        await admin.CreateTransactionAsync(account, -20m, dog, Today.AddDays(-1), payeeName: "Leclerc");

        var suggestion = await admin.GetJsonAsync("/api/suggestions?payeeName=%20leclerc");
        suggestion.GetProperty("categoryId").GetGuid().ShouldBe(groceries);

        var unknown = await admin.GetJsonAsync("/api/suggestions?payeeName=Inconnu");
        unknown.GetProperty("categoryId").ValueKind.ShouldBe(JsonValueKind.Null);
        unknown.GetProperty("payeeName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Similar_transactions_are_proposed_and_corrected_in_bulk()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2026, 1, 1));
        var groceries = await admin.CategoryIdAsync("Courses");
        await ImportAsync(admin, account, Ofx(
            (new DateOnly(2026, 11, 1), -3.20m, "B1", "CB BOULANGERIE PAUL 01/11"),
            (new DateOnly(2026, 11, 8), -4.10m, "B2", "CB BOULANGERIE PAUL 08/11"),
            (new DateOnly(2026, 11, 10), -12.00m, "P1", "CB PHARMACIE 10/11"),
            (new DateOnly(2026, 11, 15), -2.90m, "B3", "CB BOULANGERIE PAUL 15/11")));
        var latest = (await TransactionsAsync(admin, account))[0];

        var corrected = await CorrectAsync(admin, latest, "Boulangerie Paul", groceries);
        var id = corrected.GetProperty("id").GetGuid();

        var similar = (await admin.GetJsonAsync($"/api/transactions/{id}/similar")).EnumerateArray().ToList();
        similar.Count.ShouldBe(2);
        similar.ShouldAllBe(t => t.GetProperty("label").GetString()!.StartsWith("CB BOULANGERIE", StringComparison.Ordinal));

        var result = await admin.CreateAsync("/api/transactions/bulk", new
        {
            ids = similar.Select(t => t.GetProperty("id").GetGuid()).ToArray(),
            category = new { categoryId = groceries },
            payeeName = "Boulangerie Paul",
        });

        result.GetProperty("updated").GetInt32().ShouldBe(2);
        result.GetProperty("skipped").GetInt32().ShouldBe(0);
        (await admin.GetJsonAsync($"/api/transactions/{id}/similar")).GetArrayLength().ShouldBe(0);
        var bakery = await admin.GetJsonAsync($"/api/transactions?accountId={account}&categoryId={groceries}");
        bakery.GetProperty("items").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task A_rule_can_be_deleted_by_a_writer_only()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2026, 1, 1));
        await ImportAsync(admin, account, Ofx((new DateOnly(2026, 11, 20), -9.99m, "N1", "PRLV SEPA NETFLIX")));
        await CorrectAsync(admin, (await TransactionsAsync(admin, account))[0], "Netflix", await admin.CategoryIdAsync("Internet"));
        var rule = (await admin.GetJsonAsync("/api/rules"))[0];
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        (await reader.DeleteAsync($"/api/rules/{rule.GetProperty("id").GetGuid()}", rule.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/rules/{rule.GetProperty("id").GetGuid()}", rule.Version()))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetJsonAsync("/api/rules")).GetArrayLength().ShouldBe(0);
        // La règle supprimée ne modifie pas l'opération corrigée.
        (await TransactionsAsync(admin, account))[0].GetProperty("payeeName").GetString().ShouldBe("Netflix");
    }
}
