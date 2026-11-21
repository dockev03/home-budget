using HomeBudget.Api.Infrastructure.Backups;
using HomeBudget.Tests.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Api;

public sealed class BackupTests
{
    [Fact]
    public async Task Backup_is_a_readable_copy_of_the_database_and_old_ones_are_rotated()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        await admin.CreateAccountAsync("Courant", 123.45m);

        var folder = Path.Combine(factory.DataPath, "backups");
        Directory.CreateDirectory(folder);
        // Anciennes sauvegardes factices : 30 jours avant aujourd'hui, jusqu'à 10 jours avant.
        var service = factory.Services.GetRequiredService<BackupService>();
        for (var i = 10; i <= 30; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, $"homebudget-{service.Today.AddDays(-i):yyyyMMdd}.db"), "x", TestContext.Current.CancellationToken);
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "autre-fichier.txt"), "x", TestContext.Current.CancellationToken);

        var path = await service.RunAsync(TestContext.Current.CancellationToken);

        Path.GetFileName(path).ShouldBe($"homebudget-{service.Today:yyyyMMdd}.db");
        await using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name FROM Accounts";
            (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe("Courant");
        }

        // 7 quotidiennes, 4 hebdomadaires et 12 mensuelles au plus : bien moins que les 22 fichiers.
        var remaining = service.List();
        remaining.ShouldContain(service.Today);
        remaining.Count.ShouldBeLessThan(22);
        remaining.Count.ShouldBeGreaterThanOrEqualTo(7);
        File.Exists(Path.Combine(folder, "autre-fichier.txt")).ShouldBeTrue();
        Directory.EnumerateFiles(folder, ".tmp-*").ShouldBeEmpty();
    }

    [Fact]
    public void Next_run_is_today_or_tomorrow_at_the_configured_hour()
    {
        var offset = TimeSpan.FromHours(1);

        BackupWorker.DelayUntilNextRun(new DateTimeOffset(2026, 11, 21, 1, 30, 0, offset), 3).ShouldBe(TimeSpan.FromMinutes(90));
        BackupWorker.DelayUntilNextRun(new DateTimeOffset(2026, 11, 21, 3, 0, 0, offset), 3).ShouldBe(TimeSpan.FromHours(24));
        BackupWorker.DelayUntilNextRun(new DateTimeOffset(2026, 11, 21, 23, 0, 0, offset), 3).ShouldBe(TimeSpan.FromHours(4));
    }
}
