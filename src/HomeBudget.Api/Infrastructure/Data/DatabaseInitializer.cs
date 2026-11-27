using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Domain.Users;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applique les migrations, active le mode WAL et crée les données de référence (rôles, paramètres, catégories par défaut).
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

        var settings = await db.HouseholdSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new HouseholdSettings();
            db.HouseholdSettings.Add(settings);
        }

        if (!settings.DefaultCategoriesCreated)
        {
            var order = 0;
            foreach (var definition in DefaultCategories.All)
            {
                db.Categories.Add(new Category
                {
                    Name = definition.Name,
                    Kind = definition.Kind,
                    Icon = definition.Icon,
                    Color = definition.Color,
                    FamilyRole = definition.FamilyRole,
                    SortOrder = order++,
                });
            }
            settings.DefaultCategoriesCreated = true;
        }
        await db.SaveChangesAsync(ct);
    }
}
