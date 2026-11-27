using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;
using HomeBudget.Api.Infrastructure.Webhooks;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Integrations;

/// <param name="ConsecutiveFailures">Envois abandonnés à la suite ; le webhook est désactivé au 5e.</param>
public sealed record WebhookDto(
    Guid Id, string Name, string Url, IReadOnlyList<string> Events, bool IsActive, int ConsecutiveFailures,
    Guid OwnerId, DateTimeOffset CreatedAt, int Version);

/// <param name="Secret">Secret de signature : affiché une seule fois.</param>
public sealed record WebhookSecretDto(WebhookDto Item, string Secret);

public sealed record WebhookRequest(
    [property: Required, StringLength(Webhook.MaxNameLength, MinimumLength = 1)] string Name,
    [property: Required, StringLength(Webhook.MaxUrlLength, MinimumLength = 1)] string Url,
    [property: Required, MinLength(1)] IReadOnlyList<string> Events,
    bool IsActive = true);

public sealed record WebhookDeliveryDto(
    Guid Id, string Event, WebhookDeliveryStatus Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    DateTimeOffset? NextAttemptAt, int? StatusCode, string? Error);

/// <summary>
/// Webhooks (I5), réservés aux administrateurs : abonnement d'une adresse aux événements, signés en HMAC-SHA256.
/// Le webhook ne reçoit que ce que voit son créateur (R8).
/// </summary>
public static class WebhookEndpoints
{
    private const int HistorySize = 50;

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks").WithTags("Webhooks").RequireAuthorization(AuthorizationPolicies.Admin);
        group.MapGet("/", ListAsync)
            .WithSummary("Webhooks configurés.");
        group.MapGet("/events", () => TypedResults.Ok(WebhookEvents.All))
            .WithSummary("Événements auxquels un webhook peut s'abonner.");
        group.MapPost("/", CreateAsync)
            .WithSummary("Crée un webhook. Son secret de signature n'est montré qu'une fois.");
        group.MapPut("/{id:guid}", UpdateAsync)
            .Produces<WebhookDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un webhook (If-Match requis). Le réactiver remet son compteur d'échecs à zéro.");
        group.MapDelete("/{id:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un webhook et sa file d'attente (If-Match requis).");
        group.MapPost("/{id:guid}/secret", RegenerateSecretAsync)
            .Produces<WebhookSecretDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Remplace le secret de signature ; l'ancien est refusé dès maintenant.");
        group.MapPost("/{id:guid}/test", TestAsync)
            .Produces<WebhookDeliveryDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Envoie aussitôt un événement « ping » et renvoie le résultat.");
        group.MapGet("/{id:guid}/deliveries", DeliveriesAsync)
            .Produces<List<WebhookDeliveryDto>>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("50 derniers envois, du plus récent au plus ancien (conservés 30 jours).");
        return app;
    }

    private static async Task<Ok<List<WebhookDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var webhooks = await db.Webhooks.AsNoTracking().OrderBy(w => w.Id).ToListAsync(ct);
        return TypedResults.Ok(webhooks.ConvertAll(ToDto));
    }

    private static async Task<Results<Created<WebhookSecretDto>, ValidationProblem>> CreateAsync(
        WebhookRequest request, ClaimsPrincipal user, AppDbContext db, WebhookSecrets secrets, AuditTrail audit,
        IOptions<HomeBudgetOptions> options, CancellationToken ct)
    {
        if (Validate(request, options.Value.Webhooks) is { } problem)
        {
            return problem;
        }
        var secret = WebhookSigner.GenerateSecret();
        var webhook = new Webhook
        {
            Name = request.Name.Trim(),
            Url = request.Url.Trim(),
            Events = [.. request.Events.Distinct()],
            IsActive = request.IsActive,
            ProtectedSecret = secrets.Protect(secret),
            OwnerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };
        db.Webhooks.Add(webhook);
        audit.Add(AuditAction.WebhookCreated, webhook.Name, webhook.Id, $"{webhook.Url} ({string.Join(", ", webhook.Events)})");
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/webhooks/{webhook.Id}", new WebhookSecretDto(ToDto(webhook), secret));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, WebhookRequest request, HttpRequest http, AppDbContext db, AuditTrail audit, IOptions<HomeBudgetOptions> options,
        CancellationToken ct)
    {
        var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, webhook) is { } precondition)
        {
            return precondition;
        }
        if (Validate(request, options.Value.Webhooks) is { } problem)
        {
            return problem;
        }
        webhook.Name = request.Name.Trim();
        webhook.Url = request.Url.Trim();
        webhook.Events = [.. request.Events.Distinct()];
        if (request.IsActive && !webhook.IsActive)
        {
            webhook.ConsecutiveFailures = 0;
        }
        webhook.IsActive = request.IsActive;
        audit.Add(AuditAction.WebhookUpdated, webhook.Name, webhook.Id,
            $"{webhook.Url} ({string.Join(", ", webhook.Events)}){(webhook.IsActive ? string.Empty : ", inactif")}");
        if (!await db.TrySaveAsync())
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok(ToDto(webhook));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, webhook) is { } precondition)
        {
            return precondition;
        }
        db.Webhooks.Remove(webhook);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<Results<Ok<WebhookSecretDto>, NotFound>> RegenerateSecretAsync(
        Guid id, AppDbContext db, WebhookSecrets secrets, AuditTrail audit, CancellationToken ct)
    {
        var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return TypedResults.NotFound();
        }
        var secret = WebhookSigner.GenerateSecret();
        webhook.ProtectedSecret = secrets.Protect(secret);
        audit.Add(AuditAction.WebhookUpdated, webhook.Name, webhook.Id, "Secret de signature régénéré.");
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new WebhookSecretDto(ToDto(webhook), secret));
    }

    private static async Task<Results<Ok<WebhookDeliveryDto>, NotFound>> TestAsync(
        Guid id, AppDbContext db, WebhookSender sender, TimeProvider clock, CancellationToken ct)
    {
        var webhook = await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return TypedResults.NotFound();
        }
        var now = clock.GetUtcNow();
        var delivery = new WebhookDelivery
        {
            WebhookId = webhook.Id,
            Event = WebhookEvents.Ping,
            Payload = string.Empty,
            CreatedAt = now,
            NextAttemptAt = now,
        };
        delivery.Payload = JsonSerializer.Serialize(
            new WebhookMessage(delivery.Id, WebhookEvents.Ping, now, new { webhookId = webhook.Id, name = webhook.Name }),
            WebhookEventInterceptor.Json);
        var result = await sender.SendAsync(webhook, delivery, ct);
        delivery.Attempts = 1;
        delivery.Status = result.Success ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;
        delivery.CompletedAt = clock.GetUtcNow();
        delivery.LastStatusCode = result.StatusCode;
        delivery.LastError = result.Error;
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(delivery));
    }

    private static async Task<Results<Ok<List<WebhookDeliveryDto>>, NotFound>> DeliveriesAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Webhooks.AnyAsync(w => w.Id == id, ct))
        {
            return TypedResults.NotFound();
        }
        var deliveries = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.WebhookId == id)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Take(HistorySize)
            .ToListAsync(ct);
        return TypedResults.Ok(deliveries.ConvertAll(ToDto));
    }

    /// <summary>Adresse HTTP(S) absolue, sans identifiants ; une adresse IP littérale doit être autorisée.</summary>
    internal static string? CheckUrl(string url, WebhookOptions options)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "Adresse HTTP ou HTTPS complète attendue (https://…).";
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "L'adresse ne doit contenir ni identifiants ni fragment (#).";
        }
        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && !WebhookAddressPolicy.IsAllowed(ip, options.AllowPrivateNetworks)))
        {
            return options.AllowPrivateNetworks
                ? "Adresse refusée : boucle locale, link-local et métadonnées cloud sont interdites."
                : "Adresse refusée : seules les adresses publiques sont autorisées (HB__Webhooks__AllowPrivateNetworks=false).";
        }
        return null;
    }

    private static ValidationProblem? Validate(WebhookRequest request, WebhookOptions options)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Le nom est obligatoire."];
        }
        if (CheckUrl(request.Url, options) is { } urlError)
        {
            errors["url"] = [urlError];
        }
        if (request.Events.Count == 0 || request.Events.Any(e => !WebhookEvents.All.Contains(e)))
        {
            errors["events"] = [$"Événements possibles : {string.Join(", ", WebhookEvents.All)}."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static WebhookDto ToDto(Webhook w) =>
        new(w.Id, w.Name, w.Url, w.Events, w.IsActive, w.ConsecutiveFailures, w.OwnerId, w.CreatedAt, w.Version);

    private static WebhookDeliveryDto ToDto(WebhookDelivery d) =>
        new(d.Id, d.Event, d.Status, d.Attempts, d.CreatedAt, d.CompletedAt,
            d.Status == WebhookDeliveryStatus.Pending ? d.NextAttemptAt : null, d.LastStatusCode, d.LastError);
}
