using System.Text.Json;

namespace HomeBudget.Tests.Infrastructure;

/// <summary>Raccourcis de création de données via l'API.</summary>
public static class TestScenario
{
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    public static async Task<Guid> CreateAccountAsync(
        this ApiClient client, string name, decimal initialBalance = 0, string type = "Checking", DateOnly? initialDate = null)
    {
        var json = await client.CreateAsync("/api/accounts", new
        {
            name,
            type,
            initialBalance,
            initialDate = (initialDate ?? Today.AddYears(-1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        });
        return json.GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CategoryIdAsync(this ApiClient client, string name)
    {
        var categories = await client.GetJsonAsync("/api/categories");
        return categories.EnumerateArray().First(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateCategoryAsync(this ApiClient client, string name, string kind, Guid? parentId = null)
    {
        var json = await client.CreateAsync("/api/categories", new { name, kind, parentId });
        return json.GetProperty("id").GetGuid();
    }

    public static Task<JsonElement> CreateTransactionAsync(
        this ApiClient client, Guid accountId, decimal amount, Guid? categoryId = null, DateOnly? date = null,
        string? payeeName = null, string? label = null) =>
        client.CreateAsync("/api/transactions", new
        {
            accountId,
            date = Iso(date ?? Today),
            payeeName,
            label,
            lines = new[] { new { categoryId, amount, memo = (string?)null } },
        });

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static decimal Decimal(this JsonElement json, string property) => json.GetProperty(property).GetDecimal();

    public static int Version(this JsonElement json) => json.GetProperty("version").GetInt32();
}
