using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applique les migrations, active le mode WAL et crée les données de référence (rôles, paramètres).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new AppRole(role));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Création du rôle {role} impossible : {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
        }

        if (!await db.HouseholdSettings.AnyAsync(ct))
        {
            db.HouseholdSettings.Add(new HouseholdSettings());
            await db.SaveChangesAsync(ct);
        }
    }
}
