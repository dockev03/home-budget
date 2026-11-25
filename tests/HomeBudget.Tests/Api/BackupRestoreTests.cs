using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using HomeBudget.Api.Features.Backups;
using HomeBudget.Api.Infrastructure.Backups;
using HomeBudget.Tests.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

public sealed class BackupRestoreTests
{
    private static async Task SeedAsync(ApiClient admin)
    {
        var checking = await admin.CreateAccountAsync("Courant", 1500m);
        var livret = await admin.CreateAccountAsync("Livret A", 200m, "Savings");
        var restaurants = await admin.CategoryIdAsync("Restaurants");
        var cinema = await admin.CreateCategoryAsync("Cinéma", "Variable", restaurants);
        var groceries = await admin.CategoryIdAsync("Courses");
        await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 450m }, null);
        await admin.CreateAsync("/api/transactions", new
        {
            accountId = checking,
            date = TestScenario.Iso(TestScenario.Today.AddDays(-3)),
            payeeName = "Hyper U",
            label = "Courses et cinéma",
            notes = "Ventilée",
            lines = new object[]
            {
                new { categoryId = groceries, amount = -82.35m, memo = "Alimentation" },
                new { categoryId = cinema, amount = -19.90m, memo = (string?)null },
            },
        });
        await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = checking,
            toAccountId = livret,
            date = TestScenario.Iso(TestScenario.Today.AddDays(-2)),
            amount = 100m,
            label = "Épargne",
        });
        await admin.CreateAsync("/api/recurrences", new
        {
            label = "Box",
            accountId = checking,
            categoryId = await admin.CategoryIdAsync("Internet"),
            amount = -30m,
            mode = "ToValidate",
            startDate = TestScenario.Iso(TestScenario.Today),
            unit = "Month",
            interval = 1,
            end = "Never",
            isInstallment = false,
        });
    }

    private static async Task<string> ExportAsync(ApiClient client)
    {
        var response = await client.GetAsync("/api/backups/export");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldStartWith("home-budget-sauvegarde-");
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> RestoreAsync(ApiClient client, string json) =>
        client.PostContentAsync("/api/backups/restore", new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>Contenu comparable d'une sauvegarde : sans la date d'export.</summary>
    private static string Comparable(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("exportedAt");
        return node.ToJsonString();
    }

    private static string[] Snapshots(HomeBudgetFactory factory)
    {
        var folder = Path.Combine(factory.DataPath, "backups");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, $"{BackupEndpoints.SnapshotLabel}-*.db") : [];
    }

    [Fact]
    public async Task Export_then_restore_brings_back_exactly_the_exported_data()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var backup = await ExportAsync(admin);

        var document = JsonNode.Parse(backup)!;
        document["format"]!.GetValue<string>().ShouldBe("home-budget-backup");
        document["formatVersion"]!.GetValue<int>().ShouldBe(1);
        document["transactions"]!.AsArray().Count.ShouldBe(3);
        document["occurrences"]!.AsArray().Count.ShouldBeGreaterThan(0);
        // Ni comptes utilisateurs, ni mots de passe, ni secrets de double authentification.
        backup.ShouldNotContain("passwordHash", Case.Insensitive);
        backup.ShouldNotContain("securityStamp", Case.Insensitive);
        backup.ShouldNotContain(TestData.AdminUserName + "\"");

        // Changements faits après la sauvegarde : ils doivent disparaître.
        await admin.CreateAccountAsync("Compte ajouté ensuite", 42m);
        var version = (await admin.GetJsonAsync("/api/settings")).Version();
        await admin.PutAsync("/api/settings", new { periodMode = "FixedDay", periodStartDay = 25, budgetWarningPercent = 70, budgetAlertPercent = 100 }, version);

        var response = await RestoreAsync(admin, backup);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await ApiClient.ReadJsonAsync(response);
        result.GetProperty("accounts").GetInt32().ShouldBe(2);
        result.GetProperty("transactions").GetInt32().ShouldBe(3);
        Comparable(await ExportAsync(admin)).ShouldBe(Comparable(backup));
        (await admin.GetJsonAsync("/api/settings")).GetProperty("periodMode").GetString().ShouldBe("Calendar");

        // Instantané pris juste avant, avec les données d'alors, et hors rotation des sauvegardes quotidiennes.
        var snapshot = Snapshots(factory).ShouldHaveSingleItem();
        Path.GetFileName(snapshot).ShouldBe(result.GetProperty("snapshot").GetString());
        await using (var connection = new SqliteConnection($"Data Source={snapshot};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Accounts WHERE Name = 'Compte ajouté ensuite'";
            (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(1L);
        }
        factory.Services.GetRequiredService<BackupService>().List().ShouldBeEmpty();
    }

    [Fact]
    public async Task Restore_on_another_instance_keeps_its_users_and_clears_unknown_user_references()
    {
        string backup;
        await using (var source = new HomeBudgetFactory())
        {
            var admin = await source.CreateAdminClientAsync();
            await SeedAsync(admin);
            var node = JsonNode.Parse(await ExportAsync(admin))!;
            node["accounts"]![0]!["ownerId"] = Guid.NewGuid().ToString();
            backup = node.ToJsonString();
        }

        await using var target = new HomeBudgetFactory();
        var targetAdmin = await target.CreateAdminClientAsync();
        var me = await targetAdmin.GetJsonAsync("/api/auth/me");

        (await RestoreAsync(targetAdmin, backup)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await targetAdmin.GetJsonAsync("/api/auth/me")).GetProperty("id").GetGuid().ShouldBe(me.GetProperty("id").GetGuid());
        var restored = JsonNode.Parse(await ExportAsync(targetAdmin))!;
        restored["accounts"]!.AsArray().ShouldAllBe(a => a!["ownerId"] == null);
        restored["transactions"]!.AsArray().ShouldAllBe(t => t!["audit"]!["createdBy"] == null && t["audit"]!["updatedBy"] == null);
        (await targetAdmin.GetJsonAsync("/api/accounts")).GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_file_is_rejected_without_touching_the_data()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var backup = await ExportAsync(admin);
        await admin.CreateAccountAsync("Compte ajouté ensuite", 0m);

        // Référence vers un compte absent du fichier.
        var broken = JsonNode.Parse(backup)!;
        broken["transactions"]![0]!["accountId"] = Guid.NewGuid().ToString();
        var response = await RestoreAsync(admin, broken.ToJsonString());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ApiClient.ReadJsonAsync(response);
        problem.GetProperty("errors").GetProperty("file")[0].GetString()!.ShouldContain("compte introuvable");

        // JSON illisible, autre format, version inconnue, fichier incomplet.
        (await RestoreAsync(admin, "{ \"format\": ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RestoreAsync(admin, "{\"format\":\"autre\"}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var future = JsonNode.Parse(backup)!;
        future["formatVersion"] = 99;
        (await RestoreAsync(admin, future.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var partial = JsonNode.Parse(backup)!.AsObject();
        partial.Remove("transactions");
        (await RestoreAsync(admin, partial.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Rien n'a changé, et aucun instantané n'a été pris.
        (await admin.GetJsonAsync("/api/accounts")).GetProperty("items").GetArrayLength().ShouldBe(3);
        Snapshots(factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task Too_large_file_is_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var content = new ByteArrayContent(new byte[BackupEndpoints.MaxRestoreBytes + 1]);
        content.Headers.ContentType = new("application/json");

        (await admin.PostContentAsync("/api/backups/restore", content)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Backup_and_restore_are_reserved_to_administrators()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var backup = await ExportAsync(admin);
        var member = await factory.CreateUserClientAsync("membre", "Member");

        (await member.GetAsync("/api/backups/export")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RestoreAsync(member, backup)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateApiClient().GetAsync("/api/backups/export")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
