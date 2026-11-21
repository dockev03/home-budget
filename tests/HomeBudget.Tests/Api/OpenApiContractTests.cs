using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>
/// Le client TypeScript du front est généré depuis src/HomeBudget.Web/openapi.json.
/// Ce test échoue si le contrat de l'API a changé sans que ce fichier soit régénéré :
/// lancer les tests avec UPDATE_OPENAPI=1, puis « npm run gen:api » dans le front.
/// </summary>
public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Committed_openapi_document_matches_api()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var actual = Normalize(await (await admin.GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var path = Path.Combine(FindRepositoryRoot(), "src", "HomeBudget.Web", "openapi.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1" || !File.Exists(path))
        {
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
        }

        Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe(actual,
            "Le contrat de l'API a changé : relancer les tests avec UPDATE_OPENAPI=1 puis « npm run gen:api ».");
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        // Les adresses de serveur dépendent de l'hôte de test : on les retire.
        var root = doc.RootElement.EnumerateObject().Where(p => p.Name != "servers").ToDictionary(p => p.Name, p => p.Value);
        return JsonSerializer.Serialize(root, Indented).ReplaceLineEndings("\n") + "\n";
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HomeBudget.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Racine du dépôt introuvable.");
    }
}
