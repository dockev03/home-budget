using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Setup;

/// <summary>
/// Premier lancement : création du compte administrateur, protégée par un jeton à usage unique
/// écrit dans les logs du conteneur. L'endpoint est inopérant dès qu'un utilisateur existe.
/// </summary>
public static class SetupEndpoints
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup").WithTags("Setup").AllowAnonymous();

        group.MapGet("/status", (SetupTokenStore store) => TypedResults.Ok(new SetupStatus(store.IsActive)))
            .WithSummary("Indique si le premier administrateur reste à créer.");
        group.MapPost("/", CreateAdminAsync).RequireRateLimiting(RateLimitPolicies.Auth)
            .WithSummary("Crée le premier administrateur (jeton de premier lancement requis).");

        return app;
    }

    public sealed record SetupStatus(bool Required);

    public sealed record CreateAdminRequest(
        [property: Required] string Token,
        [property: Required, StringLength(64, MinimumLength = 3)] string UserName,
        [property: EmailAddress] string? Email,
        [property: Required] string Password);

    /// <summary>Démarrage : active le jeton si aucun utilisateur n'existe et l'écrit dans les logs.</summary>
    public static async Task ActivateIfNeededAsync(IServiceProvider services, string? configuredToken, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.Users.AnyAsync())
        {
            return;
        }
        var token = scope.ServiceProvider.GetRequiredService<SetupTokenStore>().Activate(configuredToken);
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            logger.LogWarning(
                "Premier lancement : aucun utilisateur. Jeton de création de l'administrateur : {SetupToken} (valable jusqu'au prochain redémarrage).",
                token);
        }
        else
        {
            logger.LogWarning("Premier lancement : aucun utilisateur. Jeton de création imposé par la configuration (HB__SetupToken).");
        }
    }

    private static async Task<Results<Ok<CurrentUser>, ProblemHttpResult, ValidationProblem>> CreateAdminAsync(
        CreateAdminRequest request,
        SetupTokenStore store,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        ILogger<CreateAdminRequest> logger)
    {
        await Gate.WaitAsync();
        try
        {
            if (!store.IsActive || await users.Users.AnyAsync())
            {
                store.Deactivate();
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "L'application est déjà initialisée.");
            }
            if (!store.Validate(request.Token))
            {
                logger.LogWarning("Premier lancement : tentative avec un jeton invalide.");
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Jeton de premier lancement invalide.");
            }

            var user = new AppUser(request.UserName.Trim()) { Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim() };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return TypedResults.ValidationProblem(created.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }
            var role = await users.AddToRoleAsync(user, Roles.Admin);
            if (!role.Succeeded)
            {
                await users.DeleteAsync(user);
                return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Attribution du rôle administrateur impossible.");
            }

            store.Deactivate();
            logger.LogInformation("Premier lancement : administrateur {UserName} créé.", user.UserName);
            await signIn.SignInAsync(user, isPersistent: false);
            return TypedResults.Ok(await CurrentUser.FromAsync(users, user));
        }
        finally
        {
            Gate.Release();
        }
    }
}
