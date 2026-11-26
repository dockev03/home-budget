using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Users;

/// <summary>Utilisateurs du foyer et rôles (K1, K5), gérés par un administrateur.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/directory", DirectoryAsync)
            .WithSummary("Noms des utilisateurs du foyer, pour afficher l'auteur d'une opération (K2).");
        group.MapGet("/", ListAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .WithSummary("Utilisateurs du foyer avec leur rôle.");
        group.MapPut("/{id:guid}/role", ChangeRoleAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .Produces<UserDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Change le rôle d'un utilisateur ; ses sessions sont fermées. Le dernier administrateur actif reste administrateur.");
        group.MapPut("/{id:guid}/status", ChangeStatusAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .Produces<UserDto>().ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Désactive ou réactive un utilisateur ; un utilisateur désactivé perd aussitôt ses sessions.");

        return app;
    }

    public sealed record UserDto(
        Guid Id,
        string UserName,
        string? DisplayName,
        string? Email,
        string Role,
        bool IsActive,
        bool TwoFactorEnabled,
        bool TwoFactorRequired,
        DateTimeOffset CreatedAt);

    public sealed record UserSummary(Guid Id, string Name, bool IsActive);

    public sealed record RoleRequest([property: Required] string Role);

    public sealed record StatusRequest(bool IsActive);

    public static string RoleLabel(string role) => role switch
    {
        Roles.Admin => "administrateur",
        Roles.Member => "membre",
        Roles.ReadOnly => "lecture seule",
        _ => role,
    };

    private static async Task<Ok<List<UserSummary>>> DirectoryAsync(AppDbContext db) =>
        TypedResults.Ok((await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync())
            .Select(u => new UserSummary(u.Id, u.Name, u.IsActive)).ToList());

    private static async Task<Ok<List<UserDto>>> ListAsync(AppDbContext db)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
        var roles = await RolesByUserAsync(db);
        return TypedResults.Ok(users.Select(u => ToDto(u, roles.GetValueOrDefault(u.Id) ?? Roles.ReadOnly)).ToList());
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid id, RoleRequest request, ClaimsPrincipal principal, AppDbContext db,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, AuditTrail audit)
    {
        if (!Roles.All.Contains(request.Role))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = ["Rôle attendu : Admin, Member ou ReadOnly."],
            });
        }
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }
        var current = (await users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.ReadOnly;
        if (current == request.Role)
        {
            return TypedResults.Ok(ToDto(user, current));
        }
        if (current == Roles.Admin && user.IsActive && await ActiveAdminCountAsync(users) <= 1)
        {
            return LastAdmin();
        }

        var removed = await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user));
        var added = removed.Succeeded ? await users.AddToRoleAsync(user, request.Role) : removed;
        if (!added.Succeeded)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Changement de rôle impossible.");
        }
        // Les rôles sont portés par le cookie : les sessions de l'utilisateur sont fermées.
        await users.UpdateSecurityStampAsync(user);
        if (IsSelf(principal, user))
        {
            await signIn.RefreshSignInAsync(user);
        }
        await audit.RecordAsync(
            AuditAction.RoleChanged, "User", user.Id,
            $"« {user.UserName} » : {RoleLabel(current)} → {RoleLabel(request.Role)}.");
        return TypedResults.Ok(ToDto(user, request.Role));
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, StatusRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, AuditTrail audit)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.ReadOnly;
        if (user.IsActive == request.IsActive)
        {
            return TypedResults.Ok(ToDto(user, role));
        }
        if (!request.IsActive)
        {
            if (IsSelf(principal, user))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Vous ne pouvez pas désactiver votre propre compte.");
            }
            if (role == Roles.Admin && await ActiveAdminCountAsync(users) <= 1)
            {
                return LastAdmin();
            }
        }

        user.IsActive = request.IsActive;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Modification impossible.");
        }
        if (!request.IsActive)
        {
            await users.UpdateSecurityStampAsync(user);
        }
        await audit.RecordAsync(
            request.IsActive ? AuditAction.UserReactivated : AuditAction.UserDeactivated, "User", user.Id, $"« {user.UserName} ».");
        return TypedResults.Ok(ToDto(user, role));
    }

    internal static async Task<Dictionary<Guid, string>> RolesByUserAsync(AppDbContext db) =>
        (await (from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                select new { userRole.UserId, role.Name }).ToListAsync())
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Name!).OrderBy(n => Roles.All.ToList().IndexOf(n)).First());

    private static async Task<int> ActiveAdminCountAsync(UserManager<AppUser> users) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => u.IsActive);

    private static bool IsSelf(ClaimsPrincipal principal, AppUser user) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString();

    private static ProblemHttpResult LastAdmin() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Le foyer doit garder au moins un administrateur actif.",
        detail: "Nommez d'abord un autre administrateur.");

    private static UserDto ToDto(AppUser u, string role) => new(
        u.Id, u.UserName ?? string.Empty, u.DisplayName, u.Email, role, u.IsActive, u.TwoFactorEnabled, u.TwoFactorRequired, u.CreatedAt);
}
