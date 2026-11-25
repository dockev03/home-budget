using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
        // La sauvegarde automatique est testée à part (BackupTests), en l'appelant directement.
        builder.UseSetting("HB:Backup:Enabled", "false");
        // Les échéances sont traitées à la saisie de la récurrence ; le traitement de fond est testé à part.
        builder.UseSetting("HB:Recurrences:Enabled", "false");
        if (Today is { } today)
        {
            builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedTimeProvider(today)));
        }
    }

    /// <summary>Date du jour imposée à l'application (null : la vraie date).</summary>
    public DateOnly? Today { get; init; }

    public string DataPath => _dataPath;

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

    /// <summary>Crée un utilisateur avec le rôle donné et renvoie un client connecté avec son compte.</summary>
    public async Task<ApiClient> CreateUserClientAsync(string userName, string role)
    {
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser(userName);
            (await users.CreateAsync(user, TestData.AdminPassword)).Succeeded.ShouldBeTrue();
            (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        }
        var client = CreateApiClient();
        var login = await client.PostAsync("/api/auth/login", new { userName, password = TestData.AdminPassword });
        login.EnsureSuccessStatusCode();
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
