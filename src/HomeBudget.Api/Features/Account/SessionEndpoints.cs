using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Notifications;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Notifications;
using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Account;

/// <summary>
/// Sessions ouvertes de l'utilisateur connecté et révocation (N8) ; alertes de sécurité par e-mail (N9). Chaque
/// utilisateur, lecture seule compris, ne voit et ne ferme que ses propres sessions.
/// </summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/api/account/sessions").WithTags("Account");
        sessions.MapGet("/", ListAsync)
            .WithSummary("Sessions ouvertes (appareil, adresse IP, activité), la plus récente en premier.");
        sessions.MapDelete("/{id:guid}", RevokeAsync)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Ferme une session : l'appareil concerné devra se reconnecter.");
        sessions.MapPost("/revoke-others", RevokeOthersAsync)
            .WithSummary("Ferme toutes les sessions sauf celle-ci.");

        var alerts = app.MapGroup("/api/account/security-alerts").WithTags("Account");
        alerts.MapGet("/", GetAlertsAsync)
            .WithSummary("Alertes de sécurité par e-mail : connexion, verrouillage du compte.");
        alerts.MapPut("/", SetAlertsAsync)
            .Produces<SecurityAlertSettings>().ProducesValidationProblem()
            .WithSummary("Active ou désactive les alertes de sécurité par e-mail.");
        return app;
    }

    /// <param name="Browser">Navigateur et version majeure, lus dans l'en-tête User-Agent.</param>
    /// <param name="OperatingSystem">Système d'exploitation, lu dans l'en-tête User-Agent.</param>
    /// <param name="IsCurrent">Session de cette requête.</param>
    public sealed record SessionDto(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastSeenAt,
        string? IpAddress,
        string Browser,
        string OperatingSystem,
        DeviceKind Device,
        SessionOrigin Origin,
        bool IsCurrent);

    /// <param name="Revoked">Nombre de sessions fermées.</param>
    public sealed record RevokeResult(int Revoked);

    /// <param name="EmailAvailable">Envoi d'e-mails configuré sur le serveur.</param>
    /// <param name="Email">Adresse de réception (celle du compte, modifiable dans Notifications).</param>
    public sealed record SecurityAlertSettings(bool EmailAvailable, string? Email, bool OnLogin, bool OnLockout);

    public sealed record SecurityAlertRequest(bool OnLogin, bool OnLockout);

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<Results<Ok<List<SessionDto>>, UnauthorizedHttpResult>> ListAsync(
        HttpContext http, UserManager<AppUser> users, UserSessions sessions, CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(http.User);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        var current = UserSessions.CurrentId(http);
        var list = await sessions.ListAsync(user, ct);
        return TypedResults.Ok(list.ConvertAll(s =>
        {
            var agent = UserAgentInfo.Parse(s.UserAgent);
            return new SessionDto(
                s.Id, s.CreatedAt, s.LastSeenAt, s.IpAddress, agent.Browser, agent.OperatingSystem, agent.Device, s.Origin, s.Id == current);
        }));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id, HttpContext http, UserSessions sessions, AppDbContext db, AuditTrail audit, CancellationToken ct)
    {
        var userId = UserId(http.User);
        var session = await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.RevokedAt == null, ct);
        if (session is null)
        {
            return TypedResults.NotFound();
        }
        await sessions.RevokeAsync(userId, id, ct);
        var agent = UserAgentInfo.Parse(session.UserAgent);
        await audit.RecordAsync(AuditAction.SessionRevoked, "UserSession", id,
            $"Session fermée : {agent.Describe()}, {session.IpAddress ?? "IP inconnue"}.");
        return TypedResults.NoContent();
    }

    private static async Task<Ok<RevokeResult>> RevokeOthersAsync(
        HttpContext http, UserSessions sessions, AuditTrail audit, CancellationToken ct)
    {
        var userId = UserId(http.User);
        var current = UserSessions.CurrentId(http) ?? Guid.Empty;
        var revoked = await sessions.RevokeOthersAsync(userId, current, ct);
        if (revoked > 0)
        {
            await audit.RecordAsync(AuditAction.SessionRevoked, "UserSession", null,
                revoked == 1 ? "1 autre session fermée." : $"{revoked} autres sessions fermées.");
        }
        return TypedResults.Ok(new RevokeResult(revoked));
    }

    private static async Task<Results<Ok<SecurityAlertSettings>, UnauthorizedHttpResult>> GetAlertsAsync(
        ClaimsPrincipal principal, AppDbContext db, UserManager<AppUser> users, IEmailSender email, CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(principal);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(await AlertSettingsAsync(user, db, email, ct));
    }

    private static async Task<SecurityAlertSettings> AlertSettingsAsync(AppUser user, AppDbContext db, IEmailSender email, CancellationToken ct)
    {
        var preference = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct)
            ?? new NotificationPreference { UserId = user.Id };
        return new SecurityAlertSettings(email.IsConfigured, user.Email, preference.LoginEmailAlert, preference.LockoutEmailAlert);
    }

    private static async Task<IResult> SetAlertsAsync(
        SecurityAlertRequest request, ClaimsPrincipal principal, AppDbContext db, UserManager<AppUser> users, IEmailSender email,
        CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        if (request.OnLogin && string.IsNullOrWhiteSpace(user.Email))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["onLogin"] = ["Enregistrez d'abord votre adresse e-mail (menu du compte › Notifications)."],
            });
        }
        var preference = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (preference is null)
        {
            preference = new NotificationPreference { UserId = user.Id };
            db.NotificationPreferences.Add(preference);
        }
        preference.LoginEmailAlert = request.OnLogin;
        preference.LockoutEmailAlert = request.OnLockout;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await AlertSettingsAsync(user, db, email, ct));
    }
}
