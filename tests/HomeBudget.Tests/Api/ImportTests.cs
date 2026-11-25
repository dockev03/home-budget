using System.Net;
using System.Text;
using System.Text.Json;

using HomeBudget.Api.Domain.Imports;
using HomeBudget.Tests.Domain;
using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class ImportTests
{
    /// <summary>Date du jour figée : le lendemain du relevé fictif.</summary>
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static readonly DateOnly Opening = new(2026, 1, 1);

    private static HomeBudgetFactory Factory() => new() { Today = Today };

    private static string Ofx => Convert.ToBase64String(Encoding.ASCII.GetBytes(StatementReaderTests.BanquePostaleOfx));

    private static string Csv => Convert.ToBase64String(Encoding.Latin1.GetBytes(StatementReaderTests.BanquePostaleCsv));

    private static object File(Guid accountId, string content, string fileName = "releve.ofx", Guid? profileId = null) =>
        new { accountId, fileName, content, profileId };

    private static async Task<JsonElement> PreviewAsync(ApiClient client, object file) =>
        await client.CreateAsync("/api/imports/preview", file);

    private static List<JsonElement> Lines(JsonElement preview) => [.. preview.GetProperty("lines").EnumerateArray()];

    /// <summary>Importe les lignes choisies (toutes par défaut), en acceptant les échéances proposées.</summary>
    private static async Task<JsonElement> CommitAsync(
        ApiClient client, Guid accountId, string content, string fileName = "releve.ofx", Guid? profileId = null,
        IEnumerable<int>? lines = null, bool acceptMatch = true)
    {
        var preview = await PreviewAsync(client, File(accountId, content, fileName, profileId));
        var chosen = lines ?? Lines(preview).Select(l => l.GetProperty("line").GetInt32());
        return await client.CreateAsync("/api/imports", new
        {
            accountId,
            fileName,
            content,
            profileId,
            lines = chosen.Select(line => new { line, acceptMatch }).ToArray(),
        });
    }

    private static async Task<List<JsonElement>> TransactionsAsync(ApiClient client, Guid accountId) =>
        [.. (await client.GetJsonAsync($"/api/transactions?accountId={accountId}")).GetProperty("items").EnumerateArray()];

    private static object Recurrence(
        Guid accountId, decimal amount, DateOnly start, string label, string mode = "Automatic", Guid? categoryId = null,
        decimal? tolerance = null, string? toleranceUnit = null) => new
        {
            label,
            accountId,
            categoryId,
            amount,
            mode,
            startDate = TestScenario.Iso(start),
            unit = "Month",
            interval = 1,
            end = "Never",
            isInstallment = false,
            tolerance,
            toleranceUnit,
        };

    [Fact]
    public async Task Ofx_import_creates_cleared_transactions_and_reimport_skips_every_duplicate()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);

        var preview = await PreviewAsync(admin, File(account, Ofx));
        preview.GetProperty("format").GetString().ShouldBe("Ofx");
        Lines(preview).ShouldAllBe(l => l.GetProperty("status").GetString() == "New");

        var result = await CommitAsync(admin, account, Ofx);
        result.GetProperty("created").GetInt32().ShouldBe(4);
        result.GetProperty("duplicates").GetInt32().ShouldBe(0);
        var transactions = await TransactionsAsync(admin, account);
        transactions.Count.ShouldBe(4);
        transactions.ShouldAllBe(t => t.GetProperty("isCleared").GetBoolean());
        transactions[0].GetProperty("label").GetString().ShouldBe("ACHAT CB SUPERMARCHE 23.11.26");
        (await admin.GetJsonAsync($"/api/accounts/{account}")).Decimal("balance").ShouldBe(2500m - 29.99m - 15.99m - 10.50m);

        // Le même relevé, réimporté : tout est reconnu (FITID), rien n'est créé.
        Lines(await PreviewAsync(admin, File(account, Ofx))).ShouldAllBe(l => l.GetProperty("status").GetString() == "Duplicate");
        var again = await CommitAsync(admin, account, Ofx);
        again.GetProperty("created").GetInt32().ShouldBe(0);
        again.GetProperty("duplicates").GetInt32().ShouldBe(4);
        (await TransactionsAsync(admin, account)).Count.ShouldBe(4);

        var history = await admin.GetJsonAsync("/api/imports");
        history.GetArrayLength().ShouldBe(2);
        history[0].GetProperty("duplicates").GetInt32().ShouldBe(4);
        history[1].GetProperty("fileName").GetString().ShouldBe("releve.ofx");
    }

    [Fact]
    public async Task Csv_needs_a_profile_and_flags_transactions_already_entered_as_probable_duplicates()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var entered = await admin.CreateTransactionAsync(account, -29.99m, date: new DateOnly(2026, 11, 23), label: "Courses");

        var missing = await admin.PostAsync("/api/imports/preview", File(account, Csv, "releve.csv"));
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(missing)).GetProperty("errors").GetProperty("profileId")[0].GetString()!.ShouldContain("profil");

        var preview = await PreviewAsync(admin, File(account, Csv, "releve.csv", ImportProfile.BanquePostaleId));
        preview.GetProperty("profileName").GetString().ShouldBe("La Banque Postale (CSV)");
        var lines = Lines(preview);
        lines.Count.ShouldBe(4);
        var probable = lines.Single(l => l.GetProperty("status").GetString() == "ProbableDuplicate");
        probable.GetProperty("line").GetInt32().ShouldBe(8);
        probable.GetProperty("duplicateOfId").GetGuid().ShouldBe(entered.GetProperty("id").GetGuid());
        probable.GetProperty("duplicateOfLabel").GetString().ShouldBe("Courses");

        // Seules les lignes choisies sont importées.
        var result = await CommitAsync(admin, account, Csv, "releve.csv", ImportProfile.BanquePostaleId, lines: [9, 10, 11]);
        result.GetProperty("created").GetInt32().ShouldBe(3);
        (await TransactionsAsync(admin, account)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task Imported_transactions_replace_generated_pending_and_upcoming_occurrences()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        var phone = await admin.CategoryIdAsync("Forfait mobile");
        var groceries = await admin.CategoryIdAsync("Courses");
        var salary = await admin.CreateCategoryAsync("Salaire test", "Income");

        // Générée aujourd'hui (automatique), à valider aujourd'hui (5 % de tolérance), et à venir demain.
        await admin.CreateAsync("/api/recurrences", Recurrence(account, -15.99m, Today, "Forfait", categoryId: phone));
        await admin.CreateAsync("/api/recurrences",
            Recurrence(account, 2450m, Today, "Salaire", "ToValidate", salary, tolerance: 5m, toleranceUnit: "Percent"));
        var upcoming = await admin.CreateAsync("/api/recurrences", Recurrence(account, -29.99m, Today.AddDays(1), "Courses drive", categoryId: groceries));
        (await TransactionsAsync(admin, account)).Count.ShouldBe(1);
        (await admin.GetJsonAsync("/api/recurrences/pending")).GetArrayLength().ShouldBe(1);

        var preview = Lines(await PreviewAsync(admin, File(account, Ofx)));
        preview.Select(l => l.GetProperty("status").GetString()).ShouldBe(["Match", "Match", "Match", "New"]);
        preview[1].GetProperty("occurrence").GetProperty("label").GetString().ShouldBe("Forfait");
        preview[2].GetProperty("occurrence").GetProperty("amount").GetDecimal().ShouldBe(2450m);

        var result = await CommitAsync(admin, account, Ofx);
        result.GetProperty("created").GetInt32().ShouldBe(4);
        result.GetProperty("matched").GetInt32().ShouldBe(3);

        var transactions = await TransactionsAsync(admin, account);
        transactions.Count.ShouldBe(4);
        var forfait = transactions.Single(t => t.GetProperty("label").GetString() == "Forfait");
        forfait.GetProperty("date").GetString().ShouldBe("2026-11-23");
        forfait.GetProperty("isCleared").GetBoolean().ShouldBeTrue();
        forfait.GetProperty("lines")[0].GetProperty("categoryId").GetGuid().ShouldBe(phone);
        var pay = transactions.Single(t => t.GetProperty("label").GetString() == "Salaire");
        pay.Decimal("amount").ShouldBe(2500m);
        pay.GetProperty("lines")[0].GetProperty("categoryId").GetGuid().ShouldBe(salary);
        transactions.Single(t => t.GetProperty("label").GetString() == "Courses drive").GetProperty("date").GetString().ShouldBe("2026-11-24");

        // L'échéance à valider est traitée, et celle de demain ne sera pas générée : la suivante est en décembre.
        (await admin.GetJsonAsync("/api/recurrences/pending")).GetArrayLength().ShouldBe(0);
        (await admin.GetJsonAsync($"/api/recurrences/{upcoming.GetProperty("id").GetGuid()}")).GetProperty("nextDate").GetString()
            .ShouldBe("2026-12-26");
    }

    [Fact]
    public async Task Declining_the_match_imports_a_separate_transaction_and_keeps_the_occurrence()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);
        await admin.CreateAsync("/api/recurrences", Recurrence(account, -15.99m, Today, "Forfait"));

        // Hors tolérance (0 € par défaut) : 2 500 € ne correspond pas à une échéance de 2 450 €.
        await admin.CreateAsync("/api/recurrences", Recurrence(account, 2450m, Today, "Salaire", "ToValidate"));
        Lines(await PreviewAsync(admin, File(account, Ofx)))[2].GetProperty("status").GetString().ShouldBe("New");

        var result = await CommitAsync(admin, account, Ofx, lines: [2], acceptMatch: false);

        result.GetProperty("matched").GetInt32().ShouldBe(0);
        var transactions = await TransactionsAsync(admin, account);
        transactions.Count.ShouldBe(2);
        transactions.Single(t => t.GetProperty("label").GetString() == "Forfait").GetProperty("isCleared").GetBoolean().ShouldBeFalse();
        (await admin.GetJsonAsync("/api/recurrences/pending")).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: new DateOnly(2026, 11, 20));

        (await admin.PostAsync("/api/imports/preview", File(account, "pas du base64 !"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/imports/preview", File(Guid.NewGuid(), Ofx))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/imports/preview", File(account, Csv, "releve.csv", Guid.NewGuid()))).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/imports", new { accountId = account, fileName = "r.ofx", content = Ofx, lines = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Le chèque du 12/11 précède le solde initial du compte : il est signalé et écarté.
        var preview = await PreviewAsync(admin, File(account, Ofx));
        Lines(preview).Count.ShouldBe(3);
        preview.GetProperty("errors").EnumerateArray().ShouldHaveSingleItem().GetProperty("message").GetString()!.ShouldContain("solde initial");

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.PostAsync("/api/imports/preview", File(account, Ofx))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Oversized_request_is_rejected()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var content = new StringContent($"{{\"content\":\"{new string('A', 9 * 1024 * 1024)}\"}}", Encoding.UTF8, "application/json");

        (await admin.PostContentAsync("/api/imports/preview", content)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Profiles_can_be_created_updated_and_deleted()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        (await admin.GetJsonAsync("/api/import-profiles")).EnumerateArray()
            .ShouldContain(p => p.GetProperty("id").GetGuid() == ImportProfile.BanquePostaleId);

        var request = new
        {
            name = "Banque TSV",
            delimiter = "\t",
            dateFormat = "yyyy-MM-dd",
            decimalSeparator = ".",
            dateColumn = 1,
            labelColumn = 2,
            amountColumn = (int?)null,
            debitColumn = 3,
            creditColumn = 4,
        };
        var created = await admin.CreateAsync("/api/import-profiles", request);
        created.GetProperty("delimiter").GetString().ShouldBe("\t");
        (await admin.PostAsync("/api/import-profiles", request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/import-profiles", request with { name = "Autre", labelColumn = 1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var id = created.GetProperty("id").GetGuid();
        var updated = await admin.PutAsync($"/api/import-profiles/{id}", request with { name = "Banque TSV 2" }, created.Version());
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = (await ApiClient.ReadJsonAsync(updated)).Version();
        (await admin.DeleteAsync($"/api/import-profiles/{id}", created.Version())).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await admin.DeleteAsync($"/api/import-profiles/{id}", version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Recurrence_tolerance_is_validated_and_returned()
    {
        await using var factory = Factory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", initialDate: Opening);

        (await admin.PostAsync("/api/recurrences", Recurrence(account, -10m, Today, "Eau", tolerance: 150m, toleranceUnit: "Percent")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var created = await admin.CreateAsync("/api/recurrences", Recurrence(account, -10m, Today, "Eau", tolerance: 2.5m));
        created.Decimal("tolerance").ShouldBe(2.5m);
        created.GetProperty("toleranceUnit").GetString().ShouldBe("Amount");
    }
}
