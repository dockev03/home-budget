using System.Globalization;

using HomeBudget.Api.Domain.Backups;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Backups;

/// <summary>
/// Instantané cohérent de la base (<c>VACUUM INTO</c>, sans arrêter l'application) dans
/// <c>/data/backups/homebudget-AAAAMMJJ.db</c>, puis rotation selon <see cref="BackupOptions"/>.
/// </summary>
public sealed partial class BackupService(
    IServiceScopeFactory scopes,
    IOptions<HomeBudgetOptions> options,
    TimeProvider clock,
    ILogger<BackupService> logger)
{
    private const string Prefix = "homebudget-";
    private const string Extension = ".db";
    private const string DateFormat = "yyyyMMdd";

    private string Folder => options.Value.BackupsPath;

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Dates des sauvegardes présentes dans le dossier.</summary>
    public IReadOnlyList<DateOnly> List() =>
        !Directory.Exists(Folder)
            ? []
            : [.. Directory.EnumerateFiles(Folder, $"{Prefix}*{Extension}")
                .Select(f => TryParseDate(Path.GetFileName(f)))
                .OfType<DateOnly>()
                .Order()];

    /// <summary>Sauvegarde du jour (remplace celle déjà faite aujourd'hui), puis rotation.</summary>
    public async Task<string> RunAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var today = Today;
        var target = PathFor(today);
        var temporary = Path.Combine(Folder, $".tmp-{Guid.NewGuid():N}{Extension}");
        try
        {
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Paramètre lié : le chemin n'est jamais concaténé dans la requête SQL.
                await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [temporary], ct);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }

        LogBackupCreated(target);
        Rotate();
        return target;
    }

    private void Rotate()
    {
        var backup = options.Value.Backup;
        var keep = BackupRetention.Keep(List(), new BackupRetention.Policy(backup.KeepDaily, backup.KeepWeekly, backup.KeepMonthly));
        foreach (var date in List().Where(d => !keep.Contains(d)))
        {
            File.Delete(PathFor(date));
            LogBackupDeleted(date);
        }
    }

    private string PathFor(DateOnly date) =>
        Path.Combine(Folder, Prefix + date.ToString(DateFormat, CultureInfo.InvariantCulture) + Extension);

    private static DateOnly? TryParseDate(string fileName) =>
        fileName.Length == Prefix.Length + DateFormat.Length + Extension.Length
        && DateOnly.TryParseExact(fileName.AsSpan(Prefix.Length, DateFormat.Length), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Sauvegarde créée : {Path}")]
    private partial void LogBackupCreated(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rotation : sauvegarde du {Date} supprimée.")]
    private partial void LogBackupDeleted(DateOnly date);
}
