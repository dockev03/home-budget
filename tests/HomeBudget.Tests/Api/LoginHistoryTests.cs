using System.Text.Json;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Features.Account;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

/// <summary>Historique des connexions de chaque utilisateur et navigateur au journal d'audit (N6).</summary>
public sealed class LoginHistoryTests
{
    private const string FirefoxWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0";
    private const string SafariIphone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    private static async Task<ApiClient> LoginAsync(HomeBudgetFactory factory, string userAgent, string userName, string password = TestData.AdminPassword)
    {
        var client = factory.CreateApiClient();
        client.Http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        await client.PostAsync("/api/auth/login", new { userName, password });
        await client.RefreshCsrfAsync();
        return client;
    }

    private static async Task<List<JsonElement>> LoginsAsync(ApiClient client) =>
        [.. (await client.GetJsonAsync("/api/account/logins")).GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Each_user_sees_own_logins_with_browser_and_failures()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        await factory.CreateUserClientAsync("alice", "Member");
        await LoginAsync(factory, SafariIphone, TestData.AdminUserName, "mauvais mot de passe");
        var pc = await LoginAsync(factory, FirefoxWindows, TestData.AdminUserName);

        var logins = await LoginsAsync(pc);

        // Échec puis réussite de l'administrateur ; la connexion d'Alice n'apparaît pas.
        logins.Count.ShouldBe(2);
        var success = logins[0];
        success.GetProperty("action").GetString().ShouldBe("LoginSucceeded");
        success.GetProperty("browser").GetString().ShouldBe("Firefox 133");
        success.GetProperty("operatingSystem").GetString().ShouldBe("Windows");
        success.GetProperty("device").GetString().ShouldBe("Desktop");
        var failure = logins[1];
        failure.GetProperty("action").GetString().ShouldBe("LoginFailed");
        failure.GetProperty("browser").GetString().ShouldBe("Safari 17");
        failure.GetProperty("device").GetString().ShouldBe("Mobile");
        (await pc.GetJsonAsync("/api/account/logins")).GetProperty("retentionDays").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Audit_log_shows_browser_to_admin()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var pc = await LoginAsync(factory, FirefoxWindows, TestData.AdminUserName);

        var page = await pc.GetJsonAsync("/api/audit?action=LoginSucceeded");

        var entry = page.GetProperty("items").EnumerateArray().First();
        entry.GetProperty("browser").GetString().ShouldBe("Firefox 133");
        entry.GetProperty("operatingSystem").GetString().ShouldBe("Windows");
    }

    [Fact]
    public async Task Purge_removes_only_old_logins()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogs.AddRange(
            new AuditLog { At = now.AddDays(-91), Action = AuditAction.LoginSucceeded, Details = "ancienne" },
            new AuditLog { At = now.AddDays(-91), Action = AuditAction.LoginFailed, Details = "ancienne" },
            new AuditLog { At = now.AddDays(-91), Action = AuditAction.Exported, Details = "ancienne" },
            new AuditLog { At = now.AddDays(-89), Action = AuditAction.LoginSucceeded, Details = "récente" });
        await db.SaveChangesAsync(ct);

        var purged = await LoginHistoryCleanupWorker.PurgeAsync(db, now, 90, ct);

        purged.ShouldBe(2);
        var left = await db.AuditLogs.AsNoTracking().Where(l => l.Details == "ancienne" || l.Details == "récente").ToListAsync(ct);
        left.Select(l => l.Action).ShouldBe([AuditAction.Exported, AuditAction.LoginSucceeded], ignoreOrder: true);
    }
}
