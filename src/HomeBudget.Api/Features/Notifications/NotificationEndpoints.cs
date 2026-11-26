using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Notifications;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Notifications;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Notifications;

/// <summary>
/// Notifications de l'utilisateur connecté : préférences (M1, M2), abonnements push de ses appareils et envois
/// d'essai. Chaque utilisateur, lecture seule compris, ne voit et ne modifie que les siens.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications");

        group.MapGet("/", GetAsync)
            .WithSummary("Préférences de notification, appareils abonnés et disponibilité du push et de l'e-mail.");
        group.MapPut("/preferences", SetPreferencesAsync)
            .Produces<NotificationSettingsDto>().ProducesValidationProblem()
            .WithSummary("Modifie les types d'alertes push reçus, le résumé par e-mail et l'adresse de réception.");
        group.MapPost("/subscriptions", SubscribeAsync)
            .Produces<PushDeviceDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Abonne cet appareil aux notifications push (abonnement fourni par le navigateur).");
        group.MapPost("/subscriptions/unsubscribe", UnsubscribeAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Désabonne l'appareil d'adresse push donnée (celui-ci, depuis le navigateur).");
        group.MapDelete("/subscriptions/{id:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retire un appareil de la liste.");
        group.MapPost("/test", TestPushAsync).RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<TestResultDto>().ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Envoie une notification d'essai à tous les appareils de l'utilisateur.");
        group.MapPost("/test-email", TestEmailAsync).RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status502BadGateway)
            .WithSummary("Envoie un e-mail d'essai à l'adresse de l'utilisateur.");
        return app;
    }

    public sealed record PreferencesDto(
        bool BudgetAlerts, bool ForecastAlerts, bool DueTomorrowAlerts, bool PendingAlerts, EmailSummaryFrequency EmailSummary);

    /// <param name="Endpoint">Adresse push, pour reconnaître l'appareil courant.</param>
    public sealed record PushDeviceDto(Guid Id, string Endpoint, string? Device, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt);

    /// <param name="PushAvailable">Clés VAPID configurées sur le serveur.</param>
    /// <param name="VapidPublicKey">Clé publique à donner au navigateur pour s'abonner.</param>
    /// <param name="EmailAvailable">Envoi d'e-mails configuré sur le serveur.</param>
    public sealed record NotificationSettingsDto(
        bool PushAvailable,
        string? VapidPublicKey,
        bool EmailAvailable,
        string? Email,
        PreferencesDto Preferences,
        IReadOnlyList<PushDeviceDto> Devices);

    /// <param name="Email">Adresse de réception du résumé ; vide pour la retirer.</param>
    public sealed record PreferencesRequest(
        bool BudgetAlerts,
        bool ForecastAlerts,
        bool DueTomorrowAlerts,
        bool PendingAlerts,
        EmailSummaryFrequency EmailSummary,
        [property: StringLength(256)] string? Email);

    public sealed record SubscriptionKeys(
        [property: Required, StringLength(PushSubscription.MaxKeyLength)] string P256dh,
        [property: Required, StringLength(PushSubscription.MaxKeyLength)] string Auth);

    /// <param name="Device">Libellé de l'appareil (navigateur et système), facultatif.</param>
    public sealed record SubscriptionRequest(
        [property: Required, StringLength(PushSubscription.MaxEndpointLength)] string Endpoint,
        [property: Required] SubscriptionKeys Keys,
        [property: StringLength(PushSubscription.MaxDeviceLength)] string? Device);

    public sealed record UnsubscribeRequest([property: Required, StringLength(PushSubscription.MaxEndpointLength)] string Endpoint);

    /// <param name="Reached">Appareils qui ont reçu la notification.</param>
    /// <param name="Removed">Abonnements expirés, retirés.</param>
    public sealed record TestResultDto(int Reached, int Removed);

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<Ok<NotificationSettingsDto>> GetAsync(
        ClaimsPrincipal principal, AppDbContext db, UserManager<AppUser> users, WebPushSender push, IEmailSender email, CancellationToken ct) =>
        TypedResults.Ok(await SettingsAsync(UserId(principal), db, users, push, email, ct));

    private static async Task<NotificationSettingsDto> SettingsAsync(
        Guid userId, AppDbContext db, UserManager<AppUser> users, WebPushSender push, IEmailSender email, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        var preference = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? new NotificationPreference { UserId = userId };
        var devices = await db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.Id).ToListAsync(ct); // UUID v7 : ordre d'ajout
        return new NotificationSettingsDto(
            push.IsConfigured,
            push.PublicKey,
            email.IsConfigured,
            user?.Email,
            new PreferencesDto(
                preference.BudgetAlerts, preference.ForecastAlerts, preference.DueTomorrowAlerts, preference.PendingAlerts, preference.EmailSummary),
            [.. devices.Select(ToDto)]);
    }

    private static PushDeviceDto ToDto(PushSubscription s) => new(s.Id, s.Endpoint, s.Device, s.CreatedAt, s.LastSuccessAt);

    private static async Task<IResult> SetPreferencesAsync(
        PreferencesRequest request, ClaimsPrincipal principal, AppDbContext db, UserManager<AppUser> users, WebPushSender push,
        IEmailSender email, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var address = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (!Enum.IsDefined(request.EmailSummary))
        {
            errors["emailSummary"] = ["Fréquence inconnue."];
        }
        if (address is not null && !new EmailAddressAttribute().IsValid(address))
        {
            errors["email"] = ["Adresse e-mail invalide."];
        }
        if (request.EmailSummary != EmailSummaryFrequency.None && address is null)
        {
            errors["email"] = ["Indiquez l'adresse qui recevra le résumé."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var userId = UserId(principal);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        if (!string.Equals(user.Email, address, StringComparison.Ordinal))
        {
            // Adresse de réception seulement (ni connexion ni réinitialisation par e-mail) : modifiée sans changer le
            // tampon de sécurité, pour ne pas fermer les sessions de l'utilisateur comme le ferait SetEmailAsync.
            user.Email = address;
            user.NormalizedEmail = users.NormalizeEmail(address);
            user.EmailConfirmed = false;
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["email"] = [.. result.Errors.Select(e => e.Description)],
                });
            }
        }

        var preference = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preference is null)
        {
            preference = new NotificationPreference { UserId = userId };
            db.NotificationPreferences.Add(preference);
        }
        preference.BudgetAlerts = request.BudgetAlerts;
        preference.ForecastAlerts = request.ForecastAlerts;
        preference.DueTomorrowAlerts = request.DueTomorrowAlerts;
        preference.PendingAlerts = request.PendingAlerts;
        preference.EmailSummary = request.EmailSummary;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SettingsAsync(userId, db, users, push, email, ct));
    }

    private static async Task<IResult> SubscribeAsync(
        SubscriptionRequest request, ClaimsPrincipal principal, AppDbContext db, WebPushSender push, TimeProvider clock, CancellationToken ct)
    {
        if (!push.IsConfigured)
        {
            return PushUnavailable();
        }
        var errors = new Dictionary<string, string[]>();
        if (!WebPushSender.IsAllowedEndpoint(request.Endpoint))
        {
            errors["endpoint"] = ["Service push non reconnu."];
        }
        if (WebPushCrypto.DecodeKey(request.Keys.P256dh) is not { Length: 65 } key || key[0] != 0x04)
        {
            errors["keys.p256dh"] = ["Clé de l'appareil invalide."];
        }
        if (WebPushCrypto.DecodeKey(request.Keys.Auth) is not { Length: 16 })
        {
            errors["keys.auth"] = ["Secret de l'appareil invalide."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Le même navigateur peut changer d'utilisateur : l'abonnement suit la dernière session.
        var userId = UserId(principal);
        var subscription = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (subscription is null)
        {
            subscription = new PushSubscription { Endpoint = request.Endpoint, CreatedAt = clock.GetUtcNow() };
            db.PushSubscriptions.Add(subscription);
        }
        subscription.UserId = userId;
        subscription.P256dh = request.Keys.P256dh;
        subscription.Auth = request.Keys.Auth;
        subscription.Device = string.IsNullOrWhiteSpace(request.Device) ? null : request.Device.Trim();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(subscription));
    }

    private static async Task<NoContent> UnsubscribeAsync(UnsubscribeRequest request, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = UserId(principal);
        await db.PushSubscriptions.Where(s => s.UserId == userId && s.Endpoint == request.Endpoint).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = UserId(principal);
        var deleted = await db.PushSubscriptions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<IResult> TestPushAsync(
        ClaimsPrincipal principal, AppDbContext db, WebPushSender push, AlertService alerts, CancellationToken ct)
    {
        if (!push.IsConfigured)
        {
            return PushUnavailable();
        }
        var userId = UserId(principal);
        var devices = await db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(ct);
        var reached = await alerts.SendToDevicesAsync(
            devices, new PushMessage("Home Budget", "Les notifications fonctionnent sur cet appareil.", "/parametres", "test"), ct);
        var remaining = await db.PushSubscriptions.CountAsync(s => s.UserId == userId, ct);
        return TypedResults.Ok(new TestResultDto(reached, devices.Count - remaining));
    }

    private static async Task<IResult> TestEmailAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users, IEmailSender email, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!email.IsConfigured)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Envoi d'e-mails non configuré.",
                detail: "Renseignez HB__Smtp__Host et HB__Smtp__From sur le serveur.");
        }
        var user = await users.GetCurrentAsync(principal);
        if (string.IsNullOrWhiteSpace(user?.Email))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Aucune adresse e-mail.",
                detail: "Enregistrez d'abord votre adresse.");
        }
        try
        {
            await email.SendAsync(new EmailMessage(
                user.Email, "Home Budget · e-mail d'essai",
                $"Bonjour {user.Name},\n\nL'envoi d'e-mails de Home Budget fonctionne.\n",
                $"<!doctype html><html lang=\"fr\"><body><p>Bonjour {System.Net.WebUtility.HtmlEncode(user.Name)},</p><p>L'envoi d'e-mails de Home Budget fonctionne.</p></body></html>"),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggers.CreateLogger(typeof(NotificationEndpoints)).LogWarning(ex, "Échec de l'e-mail d'essai.");
            // Le détail (serveur, identifiants) reste dans les logs : il n'est pas renvoyé au navigateur.
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Envoi impossible.",
                detail: "Le serveur SMTP a refusé l'envoi ou est injoignable ; le détail est dans les logs.");
        }
        return TypedResults.NoContent();
    }

    private static IResult PushUnavailable() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Notifications push non configurées.",
        detail: "Renseignez HB__Push__VapidPublicKey et HB__Push__VapidPrivateKey sur le serveur.");
}
