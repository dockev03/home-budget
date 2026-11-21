using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Infrastructure;

/// <summary>
/// Application complète en mémoire, avec une vraie base SQLite dans un dossier temporaire
/// (les migrations sont donc testées). Les requêtes sont émises en HTTPS, comme derrière le NPM.
/// </summary>
public class HomeBudgetFactory : WebApplicationFactory<Program>
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "homebudget-tests", Guid.NewGuid().ToString("N"));

    protected virtual int AuthRequestsPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("HB:DataPath", _dataPath);
        builder.UseSetting("HB:Security:AuthRequestsPerMinute", AuthRequestsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("HB:SetupToken", TestData.SetupToken);
    }

    public ApiClient CreateApiClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        return new ApiClient(client);
    }

    public SetupTokenStore SetupTokens => Services.GetRequiredService<SetupTokenStore>();

    /// <summary>Crée l'administrateur via l'API et renvoie un client connecté.</summary>
    public async Task<ApiClient> CreateAdminClientAsync()
    {
        var client = CreateApiClient();
        var response = await client.PostAsync("/api/setup", new
        {
            token = TestData.SetupToken,
            userName = TestData.AdminUserName,
            email = "admin@example.test",
            password = TestData.AdminPassword,
        });
        response.EnsureSuccessStatusCode();
        await client.RefreshCsrfAsync();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_dataPath, recursive: true);
        }
        catch (IOException)
        {
            // Fichiers SQLite encore ouverts : le dossier temporaire sera nettoyé par le système.
        }
    }
}

public static class TestData
{
    public const string SetupToken = "test-setup-token";
    public const string AdminUserName = "admin";
    public const string AdminPassword = "correct horse battery staple";
}
