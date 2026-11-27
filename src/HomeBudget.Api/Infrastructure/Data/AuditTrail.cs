using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;

namespace HomeBudget.Api.Infrastructure.Data;

/// <summary>Écriture du journal d'audit (N6). L'auteur est l'utilisateur de la session, sauf indication contraire.</summary>
public sealed class AuditTrail(AppDbContext db, IHttpContextAccessor http, TimeProvider clock)
{
    /// <summary>Ajoute une entrée au contexte, enregistrée avec les autres modifications.</summary>
    public AuditLog Add(AuditAction action, string? target = null, Guid? targetId = null, string? details = null, AppUser? actor = null, string? actorName = null)
    {
        var principal = http.HttpContext?.User;
        var entry = new AuditLog
        {
            At = clock.GetUtcNow(),
            UserId = actor?.Id ?? (Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null),
            UserName = AuditLog.Truncate(actor?.UserName ?? actorName ?? principal?.Identity?.Name, AuditLog.MaxUserNameLength),
            Action = action,
            Target = AuditLog.Truncate(target, AuditLog.MaxTargetLength),
            TargetId = targetId,
            Details = AuditLog.Truncate(details, AuditLog.MaxDetailsLength),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = AuditLog.Truncate(NullIfEmpty(http.HttpContext?.Request.Headers.UserAgent.ToString()), AuditLog.MaxUserAgentLength),
        };
        db.AuditLogs.Add(entry);
        return entry;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Ajoute une entrée et l'enregistre aussitôt.</summary>
    public async Task RecordAsync(
        AuditAction action, string? target = null, Guid? targetId = null, string? details = null, AppUser? actor = null, string? actorName = null)
    {
        Add(action, target, targetId, details, actor, actorName);
        await db.SaveChangesAsync();
    }
}
