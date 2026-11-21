using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Infrastructure.Data;

public static class DbContextExtensions
{
    /// <summary>Enregistre ; renvoie false si un autre enregistrement concurrent a changé la version entre-temps.</summary>
    public static async Task<bool> TrySaveAsync(this AppDbContext db, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
