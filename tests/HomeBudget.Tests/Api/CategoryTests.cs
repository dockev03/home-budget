using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class CategoryTests
{
    [Fact]
    public async Task Default_categories_are_created_once()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();

        var categories = (await admin.GetJsonAsync("/api/categories")).EnumerateArray().ToList();

        categories.Count.ShouldBe(40);
        categories.First(c => c.GetProperty("name").GetString() == "Salaire Kévin").GetProperty("kind").GetString().ShouldBe("Income");
        categories.First(c => c.GetProperty("name").GetString() == "Nounou Maël").GetProperty("kind").GetString().ShouldBe("Fixed");
        categories.First(c => c.GetProperty("name").GetString() == "Courses").GetProperty("kind").GetString().ShouldBe("Variable");
    }

    [Fact]
    public async Task Subcategory_takes_parent_kind_and_depth_is_limited_to_two()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var courses = await admin.CategoryIdAsync("Courses");

        var drive = await admin.CreateAsync("/api/categories", new { name = "Drive", kind = "Income", parentId = courses });
        drive.GetProperty("kind").GetString().ShouldBe("Variable");

        var third = await admin.PostAsync("/api/categories", new { name = "Trop profond", kind = "Variable", parentId = drive.GetProperty("id").GetGuid() });
        third.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_parent_kind_and_archiving_cascade_to_children()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var parent = await admin.CreateCategoryAsync("Épargne", "Variable");
        await admin.CreateCategoryAsync("Livret de Maël", "Variable", parent);

        (await admin.PutAsync($"/api/categories/{parent}", new { name = "Épargne", kind = "Savings", isArchived = true }, 1))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var child = (await admin.GetJsonAsync("/api/categories?includeArchived=true")).EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Livret de Maël");
        child.GetProperty("kind").GetString().ShouldBe("Savings");
        child.GetProperty("isArchived").GetBoolean().ShouldBeTrue();
        (await admin.GetJsonAsync("/api/categories")).EnumerateArray().Any(c => c.GetProperty("name").GetString() == "Épargne").ShouldBeFalse();
    }

    [Fact]
    public async Task Used_category_cannot_be_deleted()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var courses = await admin.CategoryIdAsync("Courses");
        var unused = await admin.CreateCategoryAsync("Inutile", "Variable");
        await admin.CreateTransactionAsync(account, -42m, courses);

        (await admin.DeleteAsync($"/api/categories/{courses}", 1)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/categories/{unused}", 1)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
