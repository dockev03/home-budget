using System.Security.Claims;

using HomeBudget.Api.Domain;
using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HomeBudget.Api.Infrastructure.Data;

/// <summary>
/// Renseigne l'auteur et la date de création et de modification (K2), et incrémente la version
/// utilisée pour la concurrence optimiste, sur toute <see cref="AuditableEntity"/> enregistrée.
/// Chaque suppression est aussi inscrite au journal d'audit (N6, DM6).
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
        if (context is null or AppDbContext { PreserveAuditFields: true })
        {
            return;
        }
        var now = clock.GetUtcNow();
        var userId = CurrentUserId();
        var deleted = new List<AuditableEntity>();
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
                case EntityState.Deleted:
                    deleted.Add(entry.Entity);
                    break;
            }
        }

        // La révocation d'une invitation a sa propre entrée, plus parlante.
        foreach (var entity in deleted.Where(e => e is not Invitation))
        {
            context.Add(new AuditLog
            {
                At = now,
                UserId = userId,
                UserName = AuditLog.Truncate(http.HttpContext?.User.Identity?.Name, AuditLog.MaxUserNameLength),
                Action = AuditAction.Deleted,
                Target = entity.GetType().Name,
                TargetId = entity.Id,
                IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            });
        }
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
