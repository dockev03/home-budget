using System.Net;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class CategoryMergeTests
{
    private static async Task<JsonElement> CategoryAsync(ApiClient client, Guid id) =>
        (await client.GetJsonAsync("/api/categories")).EnumerateArray().First(c => c.GetProperty("id").GetGuid() == id);

    private static object Recurrence(Guid accountId, Guid categoryId) => new
    {
        label = "Magazine",
        accountId,
        categoryId,
        amount = -8m,
        mode = "Automatic",
        startDate = TestScenario.Iso(TestScenario.Today.AddDays(5)),
        unit = "Month",
        interval = 1,
        end = "Never",
        isInstallment = false,
    };

    [Fact]
    public async Task Merge_moves_everything_to_the_target_then_deletes_the_source()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var source = await admin.CreateCategoryAsync("Presse", "Variable");
        var child = await admin.CreateCategoryAsync("Magazines", "Variable", source);
        var target = await admin.CreateCategoryAsync("Loisirs divers", "Variable");
        await admin.PutAsync($"/api/budgets/categories/{source}", new { mode = "Amount", value = 20m }, null);
        await admin.CreateTransactionAsync(account, -4m, source);
        await admin.CreateTransactionAsync(account, -6m, source);
        await admin.CreateAsync("/api/recurrences", Recurrence(account, source));

        var preview = await admin.GetJsonAsync($"/api/categories/{source}/merge-preview?targetId={target}");
        preview.GetProperty("lines").GetInt32().ShouldBe(2);
        preview.GetProperty("recurrences").GetInt32().ShouldBe(1);
        preview.GetProperty("subcategories").GetInt32().ShouldBe(1);
        preview.GetProperty("budgetMoved").GetBoolean().ShouldBeTrue();

        var version = (await CategoryAsync(admin, source)).Version();
        (await admin.PostAsync($"/api/categories/{source}/merge", new { targetId = target }, null))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        var response = await admin.PostAsync($"/api/categories/{source}/merge", new { targetId = target }, version);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetJsonAsync("/api/categories")).EnumerateArray().ShouldNotContain(c => c.GetProperty("id").GetGuid() == source);
        (await CategoryAsync(admin, child)).GetProperty("parentId").GetGuid().ShouldBe(target);
        (await admin.GetJsonAsync($"/api/transactions?categoryId={target}")).GetProperty("items").GetArrayLength().ShouldBe(2);
        (await admin.GetJsonAsync("/api/recurrences")).EnumerateArray().Single().GetProperty("categoryId").GetGuid().ShouldBe(target);
    }

    [Fact]
    public async Task The_target_keeps_its_own_budget()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var source = await admin.CreateCategoryAsync("Bars", "Variable");
        var target = await admin.CreateCategoryAsync("Sorties", "Variable");
        await admin.PutAsync($"/api/budgets/categories/{source}", new { mode = "Amount", value = 20m }, null);
        await admin.PutAsync($"/api/budgets/categories/{target}", new { mode = "Amount", value = 100m }, null);

        var preview = await admin.GetJsonAsync($"/api/categories/{source}/merge-preview?targetId={target}");
        preview.GetProperty("budgetDropped").GetBoolean().ShouldBeTrue();
        var version = (await CategoryAsync(admin, source)).Version();
        (await admin.PostAsync($"/api/categories/{source}/merge", new { targetId = target }, version)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var budget = (await admin.GetJsonAsync($"/api/budgets/{TestScenario.Today:yyyy-MM}")).GetRawText();
        budget.ShouldContain("Sorties");
        budget.ShouldNotContain("Bars");
    }

    [Fact]
    public async Task Merge_is_refused_between_kinds_into_a_subcategory_of_itself_or_by_a_reader()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var source = await admin.CreateCategoryAsync("Presse", "Variable");
        var child = await admin.CreateCategoryAsync("Magazines", "Variable", source);
        var other = await admin.CreateCategoryAsync("Autre parent", "Variable");
        var otherChild = await admin.CreateCategoryAsync("Autre enfant", "Variable", other);
        var income = await admin.CategoryIdAsync("Salaire Kévin");
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        foreach (var target in new[] { income, child, source, otherChild, Guid.NewGuid() })
        {
            (await admin.GetAsync($"/api/categories/{source}/merge-preview?targetId={target}"))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        var version = (await CategoryAsync(admin, source)).Version();
        (await reader.PostAsync($"/api/categories/{source}/merge", new { targetId = other }, version))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
