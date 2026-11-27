using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Integrations;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Integrations;

/// <param name="Prefix">Début du jeton, pour le reconnaître.</param>
public sealed record ApiTokenDto(
    Guid Id, string Name, string Prefix, ApiTokenScope Scope, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt);

/// <param name="Token">Jeton complet : affiché une seule fois, il n'est pas conservé en clair.</param>
public sealed record CreatedApiTokenDto(ApiTokenDto Item, string Token);

/// <param name="ExpiresInDays">Durée de validité : 30, 90 ou 365 jours.</param>
public sealed record ApiTokenRequest(
    [property: Required, StringLength(ApiToken.MaxNameLength, MinimumLength = 1)] string Name,
    ApiTokenScope Scope,
    int ExpiresInDays);

/// <summary>
/// Jetons d'accès personnels (I5) : chaque utilisateur gère les siens depuis l'application (session obligatoire,
/// un jeton ne peut pas en créer un autre).
/// </summary>
public static class ApiTokenEndpoints
{
    public static readonly IReadOnlyList<int> AllowedDurations = [30, 90, 365];

    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tokens").WithTags("Tokens");
        group.MapGet("/", ListAsync)
            .WithSummary("Jetons d'accès personnels de l'utilisateur connecté.");
        group.MapPost("/", CreateAsync)
            .WithSummary("Crée un jeton d'accès (lecture, ou écriture si l'utilisateur peut modifier les données). Le jeton n'est montré qu'une fois.");
        group.MapDelete("/{id:guid}", RevokeAsync)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Révoque (supprime) un jeton : il est refusé dès la requête suivante.");
        return app;
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<Ok<List<ApiTokenDto>>> ListAsync(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var userId = UserId(user);
        var tokens = await db.ApiTokens.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.Id).ToListAsync(ct);
        return TypedResults.Ok(tokens.ConvertAll(ToDto));
    }

    private static async Task<Results<Created<CreatedApiTokenDto>, ValidationProblem>> CreateAsync(
        ApiTokenRequest request, ClaimsPrincipal user, AppDbContext db, AuditTrail audit, TimeProvider clock, CancellationToken ct)
    {
        var userId = UserId(user);
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Le nom est obligatoire."];
        }
        if (!AllowedDurations.Contains(request.ExpiresInDays))
        {
            errors["expiresInDays"] = ["Durée possible : 30, 90 ou 365 jours."];
        }
        if (request.Scope == ApiTokenScope.Write && !user.IsInRole(Domain.Users.Roles.Admin) && !user.IsInRole(Domain.Users.Roles.Member))
        {
            errors["scope"] = ["Votre accès est en lecture seule : le jeton ne peut pas écrire."];
        }
        if (await db.ApiTokens.CountAsync(t => t.UserId == userId, ct) >= ApiToken.MaxPerUser)
        {
            errors["name"] = [$"{ApiToken.MaxPerUser} jetons au plus : révoquez ceux qui ne servent plus."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (value, hash, prefix) = ApiTokens.Generate();
        var now = clock.GetUtcNow();
        var token = new ApiToken
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Hash = hash,
            Prefix = prefix,
            Scope = request.Scope,
            CreatedAt = now,
            ExpiresAt = now.AddDays(request.ExpiresInDays),
        };
        db.ApiTokens.Add(token);
        audit.Add(AuditAction.ApiTokenCreated, token.Name, token.Id,
            $"Jeton {(token.Scope == ApiTokenScope.Write ? "en écriture" : "en lecture")}, valable {request.ExpiresInDays} jours.");
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tokens/{token.Id}", new CreatedApiTokenDto(ToDto(token), value));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, AuditTrail audit, CancellationToken ct)
    {
        var userId = UserId(user);
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (token is null)
        {
            return TypedResults.NotFound();
        }
        db.ApiTokens.Remove(token);
        audit.Add(AuditAction.ApiTokenRevoked, token.Name, token.Id);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static ApiTokenDto ToDto(ApiToken t) => new(t.Id, t.Name, t.Prefix, t.Scope, t.CreatedAt, t.ExpiresAt, t.LastUsedAt);
}
