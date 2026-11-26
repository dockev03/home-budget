using System.Net;
using System.Text;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Rapports personnalisés (G8), rapports enregistrés et diagramme de flux (G9).</summary>
public sealed class CustomReportTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateOnly Opening = new(2026, 1, 1);

    private sealed record Data(ApiClient Admin, Guid Checking, Guid Livret, Guid Groceries, Guid Restaurants, Guid Salary, Guid Fuel);

    private static async Task<Data> SeedAsync(HomeBudgetFactory factory)
    {
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 1000m, initialDate: Opening);
        var livret = await admin.CreateAccountAsync("Livret A", 0m, "Savings", Opening);
        var groceries = await admin.CategoryIdAsync("Courses");
        var restaurants = await admin.CategoryIdAsync("Restaurants");
        var salary = await admin.CategoryIdAsync("Salaire Kévin");
        var fuel = await admin.CategoryIdAsync("Essence");

        await admin.CreateTransactionAsync(checking, 2000m, salary, new DateOnly(2026, 2, 1), "Employeur");
        await admin.CreateTransactionAsync(checking, 2000m, salary, new DateOnly(2026, 3, 1), "Employeur");
        await admin.CreateTransactionAsync(checking, -80m, groceries, new DateOnly(2026, 2, 3), "Marché");
        await admin.CreateTransactionAsync(checking, -120m, groceries, new DateOnly(2026, 3, 3), "Marché");
        await admin.CreateTransactionAsync(checking, 20m, groceries, new DateOnly(2026, 3, 4), "Marché"); // remboursement
        await admin.CreateTransactionAsync(checking, -45m, restaurants, new DateOnly(2026, 3, 5), "Pizzeria");
        await admin.CreateTransactionAsync(checking, -60m, fuel, new DateOnly(2026, 3, 6));
        await admin.CreateAsync("/api/transactions", new
        {
            accountId = checking,
            date = "2026-03-07",
            payeeName = "Supermarché",
            lines = new object[]
            {
                new { categoryId = groceries, amount = -30m, tags = new[] { "Vacances", "Été" } },
                new { categoryId = restaurants, amount = -15m, tags = new[] { "Vacances" } },
            },
        });
        await admin.CreateAsync("/api/transfers", new { fromAccountId = checking, toAccountId = livret, date = "2026-03-08", amount = 300m });
        return new Data(admin, checking, livret, groceries, restaurants, salary, fuel);
    }

    private static List<(string Label, decimal Credit, decimal Debit, decimal Net, int Count)> Rows(JsonElement report) =>
        [.. report.GetProperty("rows").EnumerateArray().Select(r =>
            (r.GetProperty("label").GetString()!, r.Decimal("credit"), r.Decimal("debit"), r.Decimal("net"), r.GetProperty("count").GetInt32()))];

    [Fact]
    public async Task Report_groups_filtered_lines_and_leaves_out_transfers_by_default()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var d = await SeedAsync(factory);

        var byCategory = await d.Admin.GetJsonAsync("/api/reports/custom?from=2026-03-01&to=2026-03-31&groupBy=Category");
        Rows(byCategory).ShouldBe(
        [
            ("Salaire Kévin", 2000m, 0m, 2000m, 1),
            ("Courses", 20m, 150m, -130m, 3),
            ("Essence", 0m, 60m, -60m, 1),
            ("Restaurants", 0m, 60m, -60m, 2),
        ]);
        byCategory.Decimal("net").ShouldBe(1750m);
        byCategory.GetProperty("count").GetInt32().ShouldBe(6);

        var withTransfers = await d.Admin.GetJsonAsync("/api/reports/custom?from=2026-03-01&to=2026-03-31&includeTransfers=true");
        withTransfers.Decimal("net").ShouldBe(1750m); // sortie du courant et entrée du livret s'annulent
        Rows(withTransfers).Single(r => r.Label == "Sans catégorie").ShouldBe(("Sans catégorie", 300m, 300m, 0m, 2));

        var byPeriod = await d.Admin.GetJsonAsync($"/api/reports/custom?range=Last3Periods&groupBy=Period&kinds=Variable");
        byPeriod.GetProperty("from").GetString().ShouldBe("2026-01-01");
        Rows(byPeriod).ShouldBe([("2026-01", 0m, 0m, 0m, 0), ("2026-02", 0m, 80m, -80m, 1), ("2026-03", 20m, 270m, -250m, 5)]);

        var byTag = await d.Admin.GetJsonAsync($"/api/reports/custom?from=2026-03-01&to=2026-03-31&groupBy=Tag&categoryIds={d.Groceries}&categoryIds={d.Restaurants}");
        Rows(byTag).ShouldBe([("Sans tag", 20m, 165m, -145m, 3), ("Vacances", 0m, 45m, -45m, 1), ("Été", 0m, 30m, -30m, 1)]);

        var tagged = await d.Admin.GetJsonAsync("/api/tags");
        var vacances = tagged.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Vacances").GetProperty("id").GetGuid();
        var byPayee = await d.Admin.GetJsonAsync($"/api/reports/custom?from=2026-03-01&to=2026-03-31&groupBy=Payee&tagIds={vacances}");
        Rows(byPayee).ShouldHaveSingleItem().ShouldBe(("Supermarché", 0m, 45m, -45m, 1));

        var bySub = await d.Admin.GetJsonAsync($"/api/reports/custom?from=2026-02-01&to=2026-03-31&groupBy=Account&accountIds={d.Livret}&includeTransfers=true");
        Rows(bySub).ShouldHaveSingleItem().ShouldBe(("Livret A", 300m, 0m, 300m, 1));
    }

    [Fact]
    public async Task Invalid_report_queries_are_rejected_and_the_report_exports_to_csv_and_excel()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var d = await SeedAsync(factory);

        (await d.Admin.GetAsync("/api/reports/custom?from=2026-03-31&to=2026-03-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await d.Admin.GetAsync("/api/reports/custom")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await d.Admin.GetAsync("/api/reports/custom?from=1990-01-01&to=2026-01-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await d.Admin.GetAsync("/api/reports/custom/export?range=CurrentPeriod&format=pdf")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var csv = await d.Admin.GetAsync("/api/reports/custom/export?range=CurrentPeriod&groupBy=Category");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Groupe;Entrées;Sorties;Solde;Opérations");
        lines[1].ShouldBe("Salaire Kévin;2000,00;0,00;2000,00;1");
        lines[^1].ShouldBe("Total;2020,00;270,00;1750,00;6");

        var xlsx = await d.Admin.GetAsync("/api/reports/custom/export?range=CurrentPeriod&format=xlsx");
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await xlsx.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))[..2].ShouldBe("PK"u8.ToArray());
        var audit = await d.Admin.GetJsonAsync("/api/audit?action=Exported");
        audit.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Saved_reports_are_shared_validated_and_follow_category_merges()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var d = await SeedAsync(factory);
        var dining = await d.Admin.CreateCategoryAsync("Sorties", "Variable");

        var saved = await d.Admin.CreateAsync("/api/reports/saved", new
        {
            name = "  Repas  ",
            groupBy = "Payee",
            range = "CurrentYear",
            from = "2026-01-01",
            categoryIds = new[] { d.Restaurants, dining, d.Restaurants },
            kinds = new[] { "Variable" },
        });
        saved.GetProperty("name").GetString().ShouldBe("Repas");
        saved.GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);
        saved.GetProperty("categoryIds").GetArrayLength().ShouldBe(2);
        saved.GetProperty("kinds")[0].GetString().ShouldBe("Variable");

        (await d.Admin.PostAsync("/api/reports/saved", new { name = "", groupBy = "Payee", range = "CurrentPeriod" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiClient.ReadJsonAsync(await d.Admin.PostAsync("/api/reports/saved", new { name = "Sans dates", groupBy = "Payee", range = "Custom" })))
            .GetProperty("errors").TryGetProperty("from", out _).ShouldBeTrue();

        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        (await reader.GetJsonAsync("/api/reports/saved")).GetArrayLength().ShouldBe(1);
        (await reader.PostAsync("/api/reports/saved", new { name = "X", groupBy = "Payee", range = "CurrentPeriod" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Fusion de « Sorties » dans « Restaurants » : le filtre ne garde que la cible.
        var category = (await d.Admin.GetJsonAsync("/api/categories")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == dining);
        (await d.Admin.PostAsync($"/api/categories/{dining}/merge", new { targetId = d.Restaurants }, category.Version())).EnsureSuccessStatusCode();
        var merged = (await d.Admin.GetJsonAsync("/api/reports/saved")).EnumerateArray().Single();
        merged.GetProperty("categoryIds").EnumerateArray().Select(c => c.GetGuid()).ShouldBe([d.Restaurants]);

        var renamed = await ApiClient.ReadJsonAsync(await d.Admin.PutAsync($"/api/reports/saved/{merged.GetProperty("id").GetGuid()}", new
        {
            name = "Repas au restaurant",
            groupBy = "Period",
            range = "Custom",
            from = "2026-01-01",
            to = "2026-03-31",
        }, merged.Version()));
        renamed.GetProperty("to").GetString().ShouldBe("2026-03-31");
        renamed.GetProperty("categoryIds").GetArrayLength().ShouldBe(0);
        (await d.Admin.DeleteAsync($"/api/reports/saved/{renamed.GetProperty("id").GetGuid()}", renamed.Version())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await d.Admin.GetJsonAsync("/api/reports/saved")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Flows_go_from_income_to_kinds_and_categories_with_the_unspent_rest()
    {
        await using var factory = new HomeBudgetFactory { Today = Today };
        var d = await SeedAsync(factory);
        var epargne = await d.Admin.CreateCategoryAsync("Épargne", "Savings");
        await d.Admin.CreateTransactionAsync(d.Checking, -100m, epargne, new DateOnly(2026, 3, 9));
        await d.Admin.CreateTransactionAsync(d.Checking, -10m, null, new DateOnly(2026, 3, 9));

        var flows = await d.Admin.GetJsonAsync("/api/reports/flows?from=2026-03");
        flows.Decimal("income").ShouldBe(2000m);
        flows.Decimal("outflows").ShouldBe(360m);
        var links = flows.GetProperty("links").EnumerateArray()
            .Select(l => (l.GetProperty("source").GetString(), l.GetProperty("target").GetString(), l.Decimal("value")))
            .ToList();
        links.ShouldContain(($"cat:{d.Salary}", "income", 2000m));
        links.ShouldContain(("income", "kind:Variable", 250m));
        links.ShouldContain(("kind:Variable", $"cat:{d.Groceries}", 130m));
        links.ShouldContain(("income", "kind:Savings", 100m));
        links.ShouldContain(("income", "uncategorized-out", 10m));
        links.ShouldContain(("income", "unspent", 1640m));
        links.ShouldNotContain(l => l.Item2 == "kind:Fixed");
        flows.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "income").GetProperty("column").GetInt32().ShouldBe(1);

        // Février et mars : 4 000 € de revenus. Sans revenu, le déficit est pris sur les réserves.
        (await d.Admin.GetJsonAsync("/api/reports/flows?from=2026-02&to=2026-03")).Decimal("income").ShouldBe(4000m);
        await d.Admin.CreateTransactionAsync(d.Checking, -50m, d.Fuel, new DateOnly(2026, 4, 2));
        var april = await d.Admin.GetJsonAsync("/api/reports/flows?from=2026-04");
        april.GetProperty("links").EnumerateArray().ShouldContain(l => l.GetProperty("source").GetString() == "reserves" && l.Decimal("value") == 50m);
        (await d.Admin.GetJsonAsync("/api/reports/flows?from=2025-01")).GetProperty("nodes").GetArrayLength().ShouldBe(0);
        (await d.Admin.GetAsync("/api/reports/flows?from=2026-03&to=2026-02")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await d.Admin.GetAsync("/api/reports/flows?from=mars")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
