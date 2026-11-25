using System.Net;
using System.Text;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class BulkAndTagTests
{
    private static List<string> Tags(JsonElement line) =>
        [.. line.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!)];

    [Fact]
    public async Task Tags_are_saved_per_line_listed_and_filtered()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var groceries = await admin.CategoryIdAsync("Courses");
        var tagged = await admin.CreateAsync("/api/transactions", new
        {
            accountId = account,
            date = TestScenario.Iso(TestScenario.Today),
            lines = new object[]
            {
                new { categoryId = groceries, amount = -40m, tags = new[] { "Vacances", " vacances ", "Été  2026" } },
                new { categoryId = groceries, amount = -10m, tags = new[] { "VACANCES" } },
            },
        });
        await admin.CreateTransactionAsync(account, -5m, groceries);

        Tags(tagged.GetProperty("lines")[0]).Count.ShouldBe(2);
        Tags(tagged.GetProperty("lines")[0]).ShouldContain("Été 2026");
        Tags(tagged.GetProperty("lines")[1]).ShouldBe(["Vacances"]);

        var tags = (await admin.GetJsonAsync("/api/tags")).EnumerateArray().ToList();
        tags.Count.ShouldBe(2);
        tags[0].GetProperty("name").GetString().ShouldBe("Vacances");
        tags[0].GetProperty("usageCount").GetInt32().ShouldBe(2);

        var holidays = tags[0].GetProperty("id").GetGuid();
        var filtered = await admin.GetJsonAsync($"/api/transactions?tagId={holidays}");
        filtered.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid()
            .ShouldBe(tagged.GetProperty("id").GetGuid());

        var csv = await admin.GetAsync($"/api/exports/transactions?format=csv&tagId={holidays}");
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        var rows = text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        rows[0].ShouldEndWith(";Tags");
        rows.Length.ShouldBe(3);
        rows[1].ShouldContain("Vacances");
        rows[1].ShouldContain("Été 2026");

        // Une modification sans tags les retire ; un tag qui n'est plus utilisé disparaît de la liste.
        var response = await admin.PutAsync($"/api/transactions/{tagged.GetProperty("id").GetGuid()}", new
        {
            accountId = account,
            date = TestScenario.Iso(TestScenario.Today),
            lines = new[] { new { categoryId = groceries, amount = -50m } },
        }, tagged.Version());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetJsonAsync("/api/tags")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_tags_are_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");

        foreach (var tags in new[] { Enumerable.Range(1, 11).Select(i => $"t{i}").ToArray(), [new string('x', 31)], [" "] })
        {
            var response = await admin.PostAsync("/api/transactions", new
            {
                accountId = account,
                date = TestScenario.Iso(TestScenario.Today),
                lines = new[] { new { categoryId = (Guid?)null, amount = -1m, tags } },
            });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Bulk_update_leaves_intact_the_transactions_a_change_does_not_apply_to()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant");
        var joint = await admin.CreateAccountAsync("Joint");
        var groceries = await admin.CategoryIdAsync("Courses");
        var dog = await admin.CategoryIdAsync("Chien");
        var simple = await admin.CreateTransactionAsync(checking, -20m, dog);
        var split = await admin.CreateAsync("/api/transactions", new
        {
            accountId = checking,
            date = TestScenario.Iso(TestScenario.Today),
            lines = new object[] { new { categoryId = groceries, amount = -30m }, new { categoryId = dog, amount = -5m } },
        });
        var transfer = await admin.CreateAsync("/api/transfers", new
        {
            fromAccountId = checking,
            toAccountId = joint,
            date = TestScenario.Iso(TestScenario.Today),
            amount = 100m,
        });
        var simpleId = simple.GetProperty("id").GetGuid();
        var splitId = split.GetProperty("id").GetGuid();
        var legId = transfer.GetProperty("fromTransactionId").GetGuid();

        var category = await admin.CreateAsync("/api/transactions/bulk", new
        {
            ids = new[] { simpleId, splitId, legId, Guid.NewGuid() },
            category = new { categoryId = groceries },
        });
        category.GetProperty("updated").GetInt32().ShouldBe(1);
        category.GetProperty("skipped").GetInt32().ShouldBe(3);

        var tagsAndCleared = await admin.CreateAsync("/api/transactions/bulk", new
        {
            ids = new[] { simpleId, splitId, legId },
            isCleared = true,
            addTags = new[] { "Vacances" },
        });
        tagsAndCleared.GetProperty("updated").GetInt32().ShouldBe(3);

        var after = await admin.GetJsonAsync($"/api/transactions/{simpleId}");
        after.GetProperty("lines")[0].GetProperty("categoryId").GetGuid().ShouldBe(groceries);
        after.GetProperty("isCleared").GetBoolean().ShouldBeTrue();
        after.Version().ShouldBe(3);
        var splitAfter = await admin.GetJsonAsync($"/api/transactions/{splitId}");
        splitAfter.GetProperty("lines").EnumerateArray().ShouldAllBe(l => Tags(l).SequenceEqual(new[] { "Vacances" }));
        splitAfter.GetProperty("lines")[1].GetProperty("categoryId").GetGuid().ShouldBe(dog);
        (await admin.GetJsonAsync($"/api/transactions/{legId}")).GetProperty("isCleared").GetBoolean().ShouldBeTrue();

        await admin.CreateAsync("/api/transactions/bulk", new { ids = new[] { simpleId }, removeTags = new[] { "vacances" }, accountId = joint });
        var moved = await admin.GetJsonAsync($"/api/transactions/{simpleId}");
        moved.GetProperty("accountId").GetGuid().ShouldBe(joint);
        Tags(moved.GetProperty("lines")[0]).ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_bulk_updates_are_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var id = (await admin.CreateTransactionAsync(account, -20m)).GetProperty("id").GetGuid();
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        (await admin.PostAsync("/api/transactions/bulk", new { ids = new[] { id } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/transactions/bulk", new { ids = new[] { id }, category = new { categoryId = Guid.NewGuid() } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/transactions/bulk", new { ids = Array.Empty<Guid>(), isCleared = true }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/transactions/bulk", new { ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray(), isCleared = true }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PostAsync("/api/transactions/bulk", new { ids = new[] { id }, isCleared = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
