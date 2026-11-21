using System.Security.Claims;

using HomeBudget.Api.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HomeBudget.Api.Infrastructure.Data;

/// <summary>
/// Renseigne l'auteur et la date de création et de modification (K2), et incrémente la version
/// utilisée pour la concurrence optimiste, sur toute <see cref="AuditableEntity"/> enregistrée.
/// </summary>
public sealed class AuditInterceptor(IHttpContextAccessor http, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        var now = clock.GetUtcNow();
        var userId = CurrentUserId();
        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    entry.Entity.Version = 1;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    entry.Entity.Version++;
                    break;
            }
        }
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
