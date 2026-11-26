using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Users;

/// <summary>
/// Invitation d'un membre (K1) : l'administrateur crée un lien à usage unique valable 48 h ; l'invité y
/// choisit son mot de passe, que l'administrateur ne voit jamais. La double authentification lui est imposée.
/// </summary>
public static class InvitationEndpoints
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invitations").WithTags("Users");

        group.MapGet("/", ListAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .WithSummary("Invitations en attente.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .WithSummary("Invite un utilisateur ; le lien n'est renvoyé qu'une fois.");
        group.MapDelete("/{id:guid}", RevokeAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .WithSummary("Révoque une invitation en attente.");
        group.MapPost("/lookup", LookupAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Vérifie un lien d'invitation.");
        group.MapPost("/accept", AcceptAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Accepte une invitation : crée le compte avec le mot de passe choisi et ouvre la session.");

        return app;
    }

    public sealed record InvitationRequest(
        [property: Required, StringLength(64, MinimumLength = 3)] string UserName,
        [property: StringLength(AppUser.MaxDisplayNameLength)] string? DisplayName,
        [property: Required] string Role);

    public sealed record InvitationDto(Guid Id, string UserName, string? DisplayName, string Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

    /// <param name="Token">Jeton du lien, à transmettre à l'invité : il n'est pas conservé par le serveur.</param>
    public sealed record CreatedInvitation(InvitationDto Invitation, string Token);

    public sealed record TokenRequest([property: Required, StringLength(100)] string Token);

    public sealed record InvitationPreview(string UserName, string? DisplayName, string Role, DateTimeOffset ExpiresAt);

    public sealed record AcceptRequest(
        [property: Required, StringLength(100)] string Token,
        [property: Required, StringLength(256)] string Password);

    private static async Task<Ok<List<InvitationDto>>> ListAsync(AppDbContext db, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var invitations = await db.Invitations.AsNoTracking().Where(i => i.AcceptedAt == null).ToListAsync();
        return TypedResults.Ok(invitations.Where(i => i.IsPending(now)).OrderBy(i => i.UserName).Select(ToDto).ToList());
    }

    private static async Task<Results<Created<CreatedInvitation>, ValidationProblem>> CreateAsync(
        InvitationRequest request, AppDbContext db, UserManager<AppUser> users, TimeProvider clock, AuditTrail audit)
    {
        var userName = request.UserName.Trim();
        var errors = new Dictionary<string, string[]>();
        if (userName.Any(c => !users.Options.User.AllowedUserNameCharacters.Contains(c)))
        {
            errors["userName"] = ["Lettres sans accent, chiffres et - . _ @ uniquement."];
        }
        else if (await users.FindByNameAsync(userName) is not null || await PendingAsync(db, clock, userName) is not null)
        {
            errors["userName"] = ["Cet identifiant est déjà utilisé."];
        }
        if (!Roles.All.Contains(request.Role))
        {
            errors["role"] = ["Rôle attendu : Admin, Member ou ReadOnly."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var token = Invitation.NewToken();
        var invitation = new Invitation
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            Role = request.Role,
            TokenHash = Invitation.Hash(token),
            ExpiresAt = clock.GetUtcNow().Add(Invitation.Lifetime),
        };
        db.Invitations.Add(invitation);
        audit.Add(AuditAction.UserInvited, "Invitation", invitation.Id, $"« {userName} » ({UserEndpoints.RoleLabel(request.Role)}).");
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/invitations/{invitation.Id}", new CreatedInvitation(ToDto(invitation), token));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(Guid id, AppDbContext db, AuditTrail audit)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.AcceptedAt == null);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }
        db.Invitations.Remove(invitation);
        audit.Add(AuditAction.InvitationRevoked, "Invitation", invitation.Id, $"« {invitation.UserName} ».");
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<InvitationPreview>, ProblemHttpResult>> LookupAsync(
        TokenRequest request, AppDbContext db, TimeProvider clock)
    {
        var invitation = await FindAsync(db, clock, request.Token);
        return invitation is null
            ? Invalid()
            : TypedResults.Ok(new InvitationPreview(invitation.UserName, invitation.DisplayName, invitation.Role, invitation.ExpiresAt));
    }

    private static async Task<Results<Ok<CurrentUser>, ProblemHttpResult, ValidationProblem>> AcceptAsync(
        AcceptRequest request, AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        TimeProvider clock, AuditTrail audit)
    {
        // Usage unique, même pour deux envois simultanés du même lien.
        await Gate.WaitAsync();
        try
        {
            var invitation = await FindAsync(db, clock, request.Token);
            if (invitation is null)
            {
                return Invalid();
            }
            if (await users.FindByNameAsync(invitation.UserName) is not null)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Cet identifiant est déjà utilisé.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = new AppUser(invitation.UserName) { DisplayName = invitation.DisplayName, TwoFactorRequired = true };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = [.. created.Errors.Select(e => e.Description)],
                });
            }
            var role = await users.AddToRoleAsync(user, invitation.Role);
            if (!role.Succeeded)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Attribution du rôle impossible.");
            }
            invitation.AcceptedAt = clock.GetUtcNow();
            invitation.AcceptedUserId = user.Id;
            audit.Add(AuditAction.InvitationAccepted, "User", user.Id, $"« {user.UserName} » ({UserEndpoints.RoleLabel(invitation.Role)}).", actor: user);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            await signIn.SignInAsync(user, isPersistent: false);
            return TypedResults.Ok(await CurrentUser.FromAsync(users, user));
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<Invitation?> FindAsync(AppDbContext db, TimeProvider clock, string token)
    {
        var hash = Invitation.Hash(token);
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash);
        return invitation is not null && invitation.IsPending(clock.GetUtcNow()) ? invitation : null;
    }

    private static async Task<Invitation?> PendingAsync(AppDbContext db, TimeProvider clock, string userName)
    {
        var normalized = userName.ToUpperInvariant();
        var candidates = await db.Invitations.AsNoTracking().Where(i => i.AcceptedAt == null).ToListAsync();
        return candidates.FirstOrDefault(i => i.IsPending(clock.GetUtcNow()) && i.UserName.ToUpperInvariant() == normalized);
    }

    private static ProblemHttpResult Invalid() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Invitation invalide ou expirée.",
        detail: "Demandez un nouveau lien à l'administrateur du foyer.");

    private static InvitationDto ToDto(Invitation i) => new(i.Id, i.UserName, i.DisplayName, i.Role, i.CreatedAt, i.ExpiresAt);
}
