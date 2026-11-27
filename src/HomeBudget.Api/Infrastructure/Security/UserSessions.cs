using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Notifications;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>
/// Sessions de connexion côté serveur (N8) : chaque cookie porte l'identifiant d'une ligne <see cref="UserSession"/>,
/// créée à la connexion et vérifiée à chaque requête. Une session révoquée (ou fermée par la déconnexion) est refusée.
/// </summary>
public sealed class UserSessions(AppDbContext db, TimeProvider clock, SecurityAlertQueue alerts, IOptions<IdentityOptions> identityOptions)
{
    /// <summary>Revendication du cookie qui désigne la session.</summary>
    public const string SessionIdClaim = "hb_sid";

    /// <summary>Durée de vie d'une session inactive : celle du cookie (glissante).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>Intervalle minimal entre deux mises à jour de la dernière activité.</summary>
    private static readonly TimeSpan LastSeenPrecision = TimeSpan.FromMinutes(5);

    private const string OriginItemKey = "hb.login-origin";
    private const string CurrentItemKey = "hb.sid";

    /// <summary>
    /// Indique que la requête est une connexion (et sa méthode) : une nouvelle session est ouverte, même si le
    /// navigateur en avait déjà une.
    /// </summary>
    public static void MarkLogin(HttpContext context, SessionOrigin origin) => context.Items[OriginItemKey] = origin;

    /// <summary>Session de la requête en cours, si elle a un cookie de session valide.</summary>
    public static Guid? CurrentId(HttpContext context) =>
        context.Items[CurrentItemKey] as Guid? ?? SessionId(context.User);

    public static Guid? SessionId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    /// <summary>
    /// Ouverture du cookie de session (connexion, ou rafraîchissement après un changement de mot de passe, de rôle
    /// ou de 2FA) : la session en cours est conservée au rafraîchissement, une nouvelle est créée à la connexion.
    /// </summary>
    public static async Task OnSigningInAsync(CookieSigningInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }
        var sessions = context.HttpContext.RequestServices.GetRequiredService<UserSessions>();
        await sessions.SignInAsync(context.HttpContext, identity);
    }

    private async Task SignInAsync(HttpContext http, ClaimsIdentity identity)
    {
        var userId = Guid.Parse(identity.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var stamp = identity.FindFirst(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
        var now = clock.GetUtcNow();
        var login = http.Items[OriginItemKey] as SessionOrigin?;
        var previous = SessionId(http.User);

        if (login is null && previous is { } reused
            && http.User.FindFirstValue(ClaimTypes.NameIdentifier) == userId.ToString()
            && await db.UserSessions.AnyAsync(s => s.Id == reused && s.UserId == userId && s.RevokedAt == null))
        {
            // Rafraîchissement : même session, nouvelle empreinte.
            await db.UserSessions.Where(s => s.Id == reused)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SecurityStamp, stamp));
            identity.AddClaim(new Claim(SessionIdClaim, reused.ToString()));
            http.Items[CurrentItemKey] = reused;
            return;
        }

        if (previous is { } replaced)
        {
            // Nouvelle connexion depuis un navigateur déjà connecté : l'ancienne session est fermée.
            await RevokeWhereAsync(db.UserSessions.Where(s => s.Id == replaced), now, CancellationToken.None);
        }
        await PurgeAsync(userId, stamp, now);
        var origin = login ?? SessionOrigin.Password;
        var session = Create(http, userId, stamp, origin, now);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        identity.AddClaim(new Claim(SessionIdClaim, session.Id.ToString()));
        http.Items[CurrentItemKey] = session.Id;
        if (login is not null)
        {
            alerts.Enqueue(new SecurityAlert(SecurityAlertKind.Login, userId, now, session.IpAddress, session.UserAgent, origin));
        }
    }

    /// <summary>
    /// Vérifie la session du cookie à chaque requête et met à jour sa dernière activité. Un cookie émis avant le suivi
    /// des sessions est repris : une session est créée et le cookie réémis, sans déconnecter l'utilisateur.
    /// </summary>
    public async Task<bool> ValidateAsync(CookieValidatePrincipalContext context, AppUser user)
    {
        var http = context.HttpContext;
        var now = clock.GetUtcNow();
        var principal = context.Principal!;
        if (principal.FindFirst(SessionIdClaim) is null)
        {
            return await AdoptLegacyAsync(context, user, now);
        }

        if (SessionId(principal) is not { } id)
        {
            return false;
        }
        var row = await db.UserSessions.AsNoTracking()
            .Where(s => s.Id == id && s.UserId == user.Id)
            .Select(s => new { s.LastSeenAt, s.IpAddress, s.UserAgent, s.RevokedAt })
            .FirstOrDefaultAsync(http.RequestAborted);
        if (row is null || row.RevokedAt is not null)
        {
            return false;
        }
        http.Items[CurrentItemKey] = id;

        var ip = IpAddress(http);
        var userAgent = UserAgent(http);
        if (now - row.LastSeenAt > LastSeenPrecision || row.IpAddress != ip || row.UserAgent != userAgent)
        {
            await db.UserSessions.Where(s => s.Id == id).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.IpAddress, ip).SetProperty(x => x.UserAgent, userAgent),
                http.RequestAborted);
        }
        return true;
    }

    /// <summary>
    /// Reprise d'un cookie émis avant le suivi des sessions. Son identifiant de session est déduit du cookie (utilisateur
    /// et date d'émission) : les requêtes parallèles d'une même page reprennent la même session, et une copie du cookie
    /// d'une session révoquée reste refusée.
    /// </summary>
    private async Task<bool> AdoptLegacyAsync(CookieValidatePrincipalContext context, AppUser user, DateTimeOffset now)
    {
        var http = context.HttpContext;
        var issued = context.Properties.IssuedUtc ?? now;
        var id = LegacySessionId(user.Id, issued);
        var revokedAt = await db.UserSessions.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.RevokedAt }).FirstOrDefaultAsync(http.RequestAborted);
        if (revokedAt is null)
        {
            var session = Create(http, user.Id, user.SecurityStamp, SessionOrigin.Legacy, now, id);
            session.CreatedAt = issued;
            db.UserSessions.Add(session);
            try
            {
                await db.SaveChangesAsync(http.RequestAborted);
            }
            catch (DbUpdateException)
            {
                // Reprise simultanée par une autre requête de la même page : la session existe déjà.
                db.Entry(session).State = EntityState.Detached;
                revokedAt = await db.UserSessions.AsNoTracking().Where(s => s.Id == id)
                    .Select(s => new { s.RevokedAt }).FirstOrDefaultAsync(http.RequestAborted);
            }
        }
        if (revokedAt?.RevokedAt is not null)
        {
            return false;
        }
        var identity = new ClaimsIdentity(context.Principal!.Identity);
        identity.AddClaim(new Claim(SessionIdClaim, id.ToString()));
        context.ReplacePrincipal(new ClaimsPrincipal(identity));
        context.ShouldRenew = true;
        http.Items[CurrentItemKey] = id;
        return true;
    }

    /// <summary>Identifiant stable de la session d'un cookie sans revendication de session.</summary>
    internal static Guid LegacySessionId(Guid userId, DateTimeOffset issuedUtc)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"legacy-session|{userId:N}|{issuedUtc.UtcTicks}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Sessions ouvertes de l'utilisateur, la plus récemment active en premier.</summary>
    public Task<List<UserSession>> ListAsync(AppUser user, CancellationToken ct)
    {
        var since = clock.GetUtcNow() - Lifetime;
        return db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null && s.SecurityStamp == user.SecurityStamp && s.LastSeenAt >= since)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Ferme la session : le cookie qui la porte est refusé dès la requête suivante. La ligne est gardée (marquée
    /// révoquée) jusqu'à l'expiration du cookie, pour qu'une copie d'un cookie repris ne soit pas reprise à nouveau.
    /// </summary>
    public Task<int> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        RevokeWhereAsync(db.UserSessions.Where(s => s.Id == sessionId && s.UserId == userId), clock.GetUtcNow(), ct);

    /// <summary>Ferme toutes les sessions de l'utilisateur sauf celle indiquée.</summary>
    public Task<int> RevokeOthersAsync(Guid userId, Guid keep, CancellationToken ct) =>
        RevokeWhereAsync(db.UserSessions.Where(s => s.UserId == userId && s.Id != keep), clock.GetUtcNow(), ct);

    private static Task<int> RevokeWhereAsync(IQueryable<UserSession> sessions, DateTimeOffset now, CancellationToken ct) =>
        sessions.Where(s => s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

    /// <summary>
    /// Retire les sessions dont le cookie ne peut plus servir : empreinte de sécurité changée, ou inactives depuis plus
    /// longtemps que la durée du cookie (sessions révoquées comprises).
    /// </summary>
    private Task<int> PurgeAsync(Guid userId, string? stamp, DateTimeOffset now)
    {
        var since = now - Lifetime;
        return db.UserSessions.Where(s => s.UserId == userId && (s.SecurityStamp != stamp || s.LastSeenAt < since)).ExecuteDeleteAsync();
    }

    private static UserSession Create(
        HttpContext http, Guid userId, string? stamp, SessionOrigin origin, DateTimeOffset now, Guid? id = null) => new()
        {
            Id = id ?? Guid.CreateVersion7(),
            UserId = userId,
            CreatedAt = now,
            LastSeenAt = now,
            IpAddress = IpAddress(http),
            UserAgent = UserAgent(http),
            Origin = origin,
            SecurityStamp = stamp,
        };

    private static string? IpAddress(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return null;
        }
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    private static string? UserAgent(HttpContext http)
    {
        var value = http.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(value) ? null
            : value.Length <= UserSession.MaxUserAgentLength ? value : value[..UserSession.MaxUserAgentLength];
    }
}
