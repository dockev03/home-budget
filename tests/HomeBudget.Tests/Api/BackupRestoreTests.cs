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
                new { categoryId = groceries, amount = -82.35m, memo = "Alimentation", tags = new[] { "Vacances", "Famille" } },
                new { categoryId = cinema, amount = -19.90m, memo = (string?)null, tags = new[] { "Vacances" } },
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
            isSubscription = true,
            businessDayShift = "Next",
        });
        var ofx = "<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>" + TestScenario.Today.AddDays(-1).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
            + "<TRNAMT>-12.34<FITID>F1<NAME>ACHAT CB BOULANGERIE</STMTTRN></BANKTRANLIST></OFX>";
        var content = Convert.ToBase64String(Encoding.ASCII.GetBytes(ofx));
        await admin.CreateAsync("/api/imports", new
        {
            accountId = checking,
            fileName = "releve.ofx",
            content,
            lines = new[] { new { line = 1, acceptMatch = true } },
        });
        // Correction de l'opération importée : une règle est apprise.
        var imported = (await admin.GetJsonAsync("/api/transactions?search=BOULANGERIE")).GetProperty("items")[0];
        await admin.PutAsync($"/api/transactions/{imported.GetProperty("id").GetGuid()}", new
        {
            accountId = checking,
            date = imported.GetProperty("date").GetString(),
            payeeName = "Boulangerie Paul",
            lines = new[] { new { categoryId = groceries, amount = -12.34m } },
            referenceMonth = TestScenario.Iso(TestScenario.Today.AddMonths(-1)),
        }, imported.Version());

        // Compte rapproché, remboursement lié à une dépense et à une avance.
        var joint = await admin.CreateAccountAsync("Joint", 100m);
        var expense = await admin.CreateTransactionAsync(joint, -40m, groceries, TestScenario.Today.AddDays(-5));
        var refund = await admin.CreateTransactionAsync(joint, 15m, groceries, TestScenario.Today.AddDays(-1));
        var advance = await admin.CreateAsync("/api/advances", new { label = "Note de frais", expectedAmount = 15m, date = TestScenario.Iso(TestScenario.Today.AddDays(-5)), isClosed = false });
        await admin.PutAsync($"/api/transactions/{refund.GetProperty("id").GetGuid()}/links", new { reimbursesId = expense.GetProperty("id").GetGuid(), advanceId = advance.GetProperty("id").GetGuid() }, null);
        await admin.CreateAsync($"/api/transactions/{expense.GetProperty("id").GetGuid()}/clear", new { isCleared = true });
        await admin.CreateAsync($"/api/transactions/{refund.GetProperty("id").GetGuid()}/clear", new { isCleared = true });
        await admin.CreateAsync($"/api/accounts/{joint}/reconciliations", new { date = TestScenario.Iso(TestScenario.Today), statementBalance = 75m });

        // Budget propre à une période et provision (étape 9).
        (await admin.PutAsync($"/api/budgets/2026-12/categories/{groceries}", new { mode = "Amount", value = 600m }, null)).EnsureSuccessStatusCode();
        await admin.CreateAsync("/api/provisions", new
        {
            label = "Taxe foncière",
            categoryId = await admin.CategoryIdAsync("Courses"),
            amount = 1200m,
            dueDate = TestScenario.Iso(TestScenario.Today.AddMonths(6)),
            isAnnual = true,
        });

        // Scénario « et si… » et stratégie de prévision (étape 10).
        var scenario = await admin.CreateAsync("/api/scenarios", new { name = "Nouvelle voiture", isActive = true });
        await admin.CreateAsync($"/api/scenarios/{scenario.GetProperty("id").GetGuid()}/events", new
        {
            label = "Crédit auto",
            accountId = checking,
            categoryId = (Guid?)null,
            amount = -250m,
            date = TestScenario.Iso(TestScenario.Today.AddMonths(1)),
            frequency = "Monthly",
            endDate = TestScenario.Iso(TestScenario.Today.AddYears(4)),
        });
        var settingsVersion = (await admin.GetJsonAsync("/api/settings")).Version();
        (await admin.PutAsync("/api/settings", new
        {
            periodMode = "Calendar",
            periodStartDay = 1,
            budgetWarningPercent = 80,
            budgetAlertPercent = 100,
            forecastStrategy = "Average",
            forecastAverageMonths = 3,
            emergencyFundAverageMonths = 4,
            envelopeMode = true,
        }, settingsVersion)).EnsureSuccessStatusCode();

        // Valorisation, objectifs d'épargne et épargne de précaution (étape 11).
        var pea = await admin.CreateAccountAsync("PEA", 5000m, "Investment");
        await admin.CreateAsync($"/api/accounts/{pea}/valuations", new { date = TestScenario.Iso(TestScenario.Today.AddDays(-1)), value = 5321.45m, note = "Relevé" });
        await admin.CreateAsync("/api/savings-goals", new { name = "Vacances", targetAmount = 2000m, targetDate = TestScenario.Iso(TestScenario.Today.AddMonths(8)), accountId = livret });
        var epargne = await admin.CreateCategoryAsync("Épargne enfant", "Savings");
        await admin.CreateAsync("/api/savings-goals", new { name = "Études", targetAmount = 10000m, categoryId = epargne, startDate = "2026-01-01" });

        // Crédit, répartition des dépenses communes et rapport enregistré (étape 12).
        var loanAccount = await admin.CreateAccountAsync("Prêt auto", -12000m, "Credit");
        (await admin.PutAsync($"/api/accounts/{loanAccount}/loan", new
        {
            principal = 12000m,
            annualRate = 3.875m,
            durationMonths = 48,
            firstPaymentDate = "2026-01-05",
            monthlyInsurance = 6.5m,
        }, null)).EnsureSuccessStatusCode();
        var me = (await admin.GetJsonAsync("/api/auth/me")).GetProperty("id").GetGuid();
        var courant = await admin.GetJsonAsync($"/api/accounts/{checking}");
        (await admin.PutAsync($"/api/accounts/{checking}", new
        {
            name = "Courant",
            type = "Checking",
            initialBalance = 1500m,
            initialDate = courant.GetProperty("initialDate").GetString(),
            holderId = me,
        }, courant.Version())).EnsureSuccessStatusCode();
        var cinemaJson = (await admin.GetJsonAsync("/api/categories")).EnumerateArray().First(c => c.GetProperty("id").GetGuid() == cinema);
        (await admin.PutAsync($"/api/categories/{cinema}", new
        {
            name = "Cinéma",
            kind = "Variable",
            parentId = restaurants,
            excludeFromSplit = true,
        }, cinemaJson.Version())).EnsureSuccessStatusCode();
        var splitVersion = (await admin.GetJsonAsync("/api/split/settings")).Version();
        (await admin.PutAsync("/api/split/settings", new { mode = "FixedShares", shares = new[] { new { userId = me, percent = 100m } } }, splitVersion))
            .EnsureSuccessStatusCode();
        await admin.CreateAsync("/api/reports/saved", new
        {
            name = "Sorties",
            groupBy = "Payee",
            range = "Last3Periods",
            categoryIds = new[] { restaurants },
            kinds = new[] { "Variable" },
        });

        // Report du reste (étape 15).
        (await admin.PutAsync($"/api/budgets/categories/{await admin.CategoryIdAsync("Courses")}/carry-over", new { enabled = true }, null))
            .EnsureSuccessStatusCode();

        // Organisme de santé (étape 16).
        var payee = (await admin.GetJsonAsync("/api/payees?search=hyper")).EnumerateArray().First().GetProperty("id").GetGuid();
        (await admin.PutAsync($"/api/payees/{payee}/reimburser", new { reimburser = "PrimaryInsurance" }, null))
            .EnsureSuccessStatusCode();
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
        // Pièce jointe (B7) : sa description fait partie de la sauvegarde, le fichier de l'archive à part.
        var withFile = (await admin.GetJsonAsync("/api/transactions")).GetProperty("items")[0].GetProperty("id").GetGuid();
        (await admin.PostContentAsync($"/api/transactions/{withFile}/attachments?fileName=fiche.pdf",
            new ByteArrayContent("%PDF-1.7 fiche"u8.ToArray()))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var backup = await ExportAsync(admin);

        var document = JsonNode.Parse(backup)!;
        document["format"]!.GetValue<string>().ShouldBe("home-budget-backup");
        document["formatVersion"]!.GetValue<int>().ShouldBe(11);
        document["settings"]!["envelopeSince"]!.GetValue<string>().ShouldNotBeNull();
        document["categories"]!.AsArray().Count(c => c!["carryOverSince"] is not null).ShouldBe(1);
        document["categories"]!.AsArray().Count(c => c!["familyRole"]!.GetValue<string>() != "None").ShouldBe(4);
        document["payees"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "Hyper U")!["reimburser"]!.GetValue<string>().ShouldBe("PrimaryInsurance");
        document["occurrences"]!.AsArray().ShouldAllBe(o => o!["expectedAmount"] != null);
        document["attachments"]!.AsArray().ShouldHaveSingleItem()!["fileName"]!.GetValue<string>().ShouldBe("fiche.pdf");
        var loan = document["loans"]!.AsArray().ShouldHaveSingleItem()!;
        loan["annualRate"]!.GetValue<decimal>().ShouldBe(3.875m);
        loan["monthlyInsurance"]!.GetValue<decimal>().ShouldBe(6.5m);
        document["memberShares"]!.AsArray().ShouldHaveSingleItem()!["percent"]!.GetValue<decimal>().ShouldBe(100m);
        document["settings"]!["splitMode"]!.GetValue<string>().ShouldBe("FixedShares");
        document["accounts"]!.AsArray().Count(a => a!["holderId"] is not null).ShouldBe(1);
        document["categories"]!.AsArray().Count(c => c!["excludeFromSplit"]!.GetValue<bool>()).ShouldBe(1);
        var report = document["savedReports"]!.AsArray().ShouldHaveSingleItem()!;
        report["kinds"]!.AsArray().ShouldHaveSingleItem()!.GetValue<string>().ShouldBe("Variable");
        report["range"]!.GetValue<string>().ShouldBe("Last3Periods");
        document["valuations"]!.AsArray().ShouldHaveSingleItem()!["value"]!.GetValue<decimal>().ShouldBe(5321.45m);
        document["savingsGoals"]!.AsArray().Count.ShouldBe(2);
        document["settings"]!["emergencyFundAverageMonths"]!.GetValue<int>().ShouldBe(4);
        document["accounts"]!.AsArray().Count(a => a!["isEmergencyFund"]!.GetValue<bool>()).ShouldBe(1);
        document["scenarios"]!.AsArray().ShouldHaveSingleItem()!["name"]!.GetValue<string>().ShouldBe("Nouvelle voiture");
        document["scenarioEvents"]!.AsArray().ShouldHaveSingleItem()!["frequency"]!.GetValue<string>().ShouldBe("Monthly");
        document["settings"]!["forecastStrategy"]!.GetValue<string>().ShouldBe("Average");
        document["settings"]!["forecastAverageMonths"]!.GetValue<int>().ShouldBe(3);
        document["periodBudgets"]!.AsArray().ShouldHaveSingleItem()!["periodKey"]!.GetValue<string>().ShouldBe("2026-12");
        document["provisions"]!.AsArray().ShouldHaveSingleItem()!["isAnnual"]!.GetValue<bool>().ShouldBeTrue();
        document["recurrences"]![0]!["businessDayShift"]!.GetValue<string>().ShouldBe("Next");
        // Correction de l'opération importée, liens, pointages et rapprochement : autant de modifications tracées.
        document["transactionChanges"]!.AsArray().Count.ShouldBeGreaterThanOrEqualTo(4);
        document["transactions"]!.AsArray().Count.ShouldBe(6);
        document["reconciliations"]!.AsArray().Count.ShouldBe(1);
        document["advances"]!.AsArray().Count.ShouldBe(1);
        document["transactions"]!.AsArray().Count(t => t!["reconciliationId"] is not null).ShouldBe(2);
        document["transactions"]!.AsArray().Count(t => t!["referenceMonth"] is not null).ShouldBe(1);
        document["recurrences"]![0]!["isSubscription"]!.GetValue<bool>().ShouldBeTrue();
        document["tags"]!.AsArray().Count.ShouldBe(2);
        document["labelRules"]!.AsArray().ShouldHaveSingleItem()!["key"]!.GetValue<string>().ShouldBe("BOULANGERIE");
        document["importBatches"]!.AsArray().Count.ShouldBe(1);
        document["importProfiles"]!.AsArray().Count.ShouldBe(1);
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
        result.GetProperty("accounts").GetInt32().ShouldBe(5);
        result.GetProperty("transactions").GetInt32().ShouldBe(6);
        result.GetProperty("attachments").GetInt32().ShouldBe(1);
        result.GetProperty("missingAttachmentFiles").GetInt32().ShouldBe(0);
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
    public async Task Version_1_backup_without_imports_is_still_restored_and_keeps_the_import_profiles()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var node = JsonNode.Parse(await ExportAsync(admin))!.AsObject();
        node["formatVersion"] = 1;
        node.Remove("valuations");
        node.Remove("savingsGoals");
        node.Remove("scenarios");
        node.Remove("scenarioEvents");
        node.Remove("importProfiles");
        node.Remove("importBatches");
        node.Remove("tags");
        node.Remove("labelRules");
        node.Remove("reconciliations");
        node.Remove("advances");
        foreach (var t in node["transactions"]!.AsArray())
        {
            t!.AsObject().Remove("rawLabel");
            t.AsObject().Remove("importBatchId");
            t.AsObject().Remove("importHash");
            t.AsObject().Remove("reconciliationId");
            t.AsObject().Remove("reimbursesId");
            t.AsObject().Remove("advanceId");
            foreach (var line in t["lines"]!.AsArray())
            {
                line!.AsObject().Remove("tagIds");
            }
        }
        foreach (var r in node["recurrences"]!.AsArray())
        {
            r!.AsObject().Remove("tolerance");
            r.AsObject().Remove("toleranceUnit");
            r.AsObject().Remove("isSubscription");
        }

        (await RestoreAsync(admin, node.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = JsonNode.Parse(await ExportAsync(admin))!;
        restored["importBatches"]!.AsArray().ShouldBeEmpty();
        restored["importProfiles"]!.AsArray().Count.ShouldBe(1);
        restored["tags"]!.AsArray().ShouldBeEmpty();
        restored["labelRules"]!.AsArray().ShouldBeEmpty();
        restored["reconciliations"]!.AsArray().ShouldBeEmpty();
        restored["advances"]!.AsArray().ShouldBeEmpty();
        restored["recurrences"]![0]!["toleranceUnit"]!.GetValue<string>().ShouldBe("Amount");
    }

    [Fact]
    public async Task Version_5_backup_without_scenarios_is_restored_with_the_budget_strategy()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var node = JsonNode.Parse(await ExportAsync(admin))!.AsObject();
        node["formatVersion"] = 5;
        node.Remove("valuations");
        node.Remove("savingsGoals");
        node.Remove("scenarios");
        node.Remove("scenarioEvents");
        node["settings"]!.AsObject().Remove("forecastStrategy");
        node["settings"]!.AsObject().Remove("forecastAverageMonths");

        (await RestoreAsync(admin, node.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = JsonNode.Parse(await ExportAsync(admin))!;
        restored["scenarios"]!.AsArray().ShouldBeEmpty();
        restored["scenarioEvents"]!.AsArray().ShouldBeEmpty();
        var settings = await admin.GetJsonAsync("/api/settings");
        settings.GetProperty("forecastStrategy").GetString().ShouldBe("Budget");
        settings.GetProperty("forecastAverageMonths").GetInt32().ShouldBe(6);

        // Une sauvegarde v6 doit contenir les scénarios.
        node["formatVersion"] = 6;
        var response = await RestoreAsync(admin, node.ToJsonString());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(response)).GetProperty("errors").GetProperty("file")[0].GetString()!.ShouldContain("scénarios");
    }

    [Fact]
    public async Task Version_6_backup_without_savings_is_restored_with_emergency_fund_following_the_account_type()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var node = JsonNode.Parse(await ExportAsync(admin))!.AsObject();
        node["formatVersion"] = 6;
        node.Remove("valuations");
        node.Remove("savingsGoals");
        node["settings"]!.AsObject().Remove("emergencyFundAverageMonths");
        foreach (var a in node["accounts"]!.AsArray())
        {
            a!.AsObject().Remove("isEmergencyFund");
        }

        (await RestoreAsync(admin, node.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = JsonNode.Parse(await ExportAsync(admin))!;
        restored["valuations"]!.AsArray().ShouldBeEmpty();
        restored["savingsGoals"]!.AsArray().ShouldBeEmpty();
        restored["accounts"]!.AsArray().Where(a => a!["isEmergencyFund"]!.GetValue<bool>())
            .ShouldHaveSingleItem()!["type"]!.GetValue<string>().ShouldBe("Savings");
        (await admin.GetJsonAsync("/api/settings")).GetProperty("emergencyFundAverageMonths").GetInt32().ShouldBe(6);

        // Une sauvegarde v7 doit contenir les valorisations et les objectifs.
        node["formatVersion"] = 7;
        var response = await RestoreAsync(admin, node.ToJsonString());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(response)).GetProperty("errors").GetProperty("file")[0].GetString()!.ShouldContain("objectifs");
    }

    [Fact]
    public async Task Version_7_backup_without_loans_nor_split_is_restored_with_equal_shares()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var node = JsonNode.Parse(await ExportAsync(admin))!.AsObject();
        node["formatVersion"] = 7;
        node.Remove("loans");
        node.Remove("memberShares");
        node.Remove("savedReports");
        node["settings"]!.AsObject().Remove("splitMode");
        foreach (var a in node["accounts"]!.AsArray())
        {
            a!.AsObject().Remove("holderId");
        }
        foreach (var c in node["categories"]!.AsArray())
        {
            c!.AsObject().Remove("excludeFromSplit");
        }

        (await RestoreAsync(admin, node.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = JsonNode.Parse(await ExportAsync(admin))!;
        restored["loans"]!.AsArray().ShouldBeEmpty();
        restored["memberShares"]!.AsArray().ShouldBeEmpty();
        restored["savedReports"]!.AsArray().ShouldBeEmpty();
        restored["accounts"]!.AsArray().ShouldAllBe(a => a!["holderId"] == null);
        restored["categories"]!.AsArray().ShouldAllBe(c => !c!["excludeFromSplit"]!.GetValue<bool>());
        (await admin.GetJsonAsync("/api/split/settings")).GetProperty("mode").GetString().ShouldBe("Equal");

        // Une sauvegarde v8 doit contenir les crédits, les parts et les rapports.
        node["formatVersion"] = 8;
        var response = await RestoreAsync(admin, node.ToJsonString());
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(response)).GetProperty("errors").GetProperty("file")[0].GetString()!.ShouldContain("crédits");
    }

    [Fact]
    public async Task Version_10_backup_gets_default_family_roles_and_expected_amounts()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var node = JsonNode.Parse(await ExportAsync(admin))!.AsObject();
        node["formatVersion"] = 10;
        foreach (var c in node["categories"]!.AsArray())
        {
            c!.AsObject().Remove("familyRole");
        }
        foreach (var p in node["payees"]!.AsArray())
        {
            p!.AsObject().Remove("reimburser");
        }
        foreach (var o in node["occurrences"]!.AsArray())
        {
            o!.AsObject().Remove("expectedAmount");
        }

        (await RestoreAsync(admin, node.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = JsonNode.Parse(await ExportAsync(admin))!;
        restored["categories"]!.AsArray().Where(c => c!["familyRole"]!.GetValue<string>() != "None")
            .Select(c => c!["name"]!.GetValue<string>()).Order().ShouldBe(["Allocation CAF", "CMG", "Nounou Maël", "Santé"]);
        restored["payees"]!.AsArray().ShouldAllBe(p => p!["reimburser"]!.GetValue<string>() == "None");
        var amounts = restored["recurrences"]!.AsArray().ToDictionary(r => r!["id"]!.GetValue<Guid>(), r => r!["amount"]!.GetValue<decimal>());
        var occurrences = restored["occurrences"]!.AsArray();
        occurrences.ShouldNotBeEmpty();
        occurrences.ShouldAllBe(o => o!["expectedAmount"]!.GetValue<decimal>() == amounts[o!["recurrenceId"]!.GetValue<Guid>()]);
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
        // Titulaire et part de l'administrateur de la source : inconnus ici, vidés ou ignorés.
        restored["accounts"]!.AsArray().ShouldAllBe(a => a!["holderId"] == null);
        restored["memberShares"]!.AsArray().ShouldBeEmpty();
        restored["transactions"]!.AsArray().ShouldAllBe(t => t!["audit"]!["createdBy"] == null && t["audit"]!["updatedBy"] == null);
        (await targetAdmin.GetJsonAsync("/api/accounts")).GetProperty("items").GetArrayLength().ShouldBe(5);
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
        (await admin.GetJsonAsync("/api/accounts")).GetProperty("items").GetArrayLength().ShouldBe(6);
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
