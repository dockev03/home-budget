using System.Security.Claims;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Attachments;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Account;

/// <summary>
/// Photo de profil de l'utilisateur connecté, affichée dans la pastille et le menu du compte. Le navigateur la recadre
/// en carré et la réduit avant l'envoi ; le serveur vérifie la taille et le type réel (JPEG, PNG ou WebP).
/// </summary>
public static class AvatarEndpoints
{
    /// <summary>Marge pour un envoi annoncé un peu plus long que le fichier.</summary>
    private const long RequestOverhead = 4 * 1024;

    public static IEndpointRouteBuilder MapAvatarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account/avatar").WithTags("Account");
        group.MapGet("/", GetAsync)
            .Produces(StatusCodes.Status200OK, contentType: FileSignature.Jpeg, additionalContentTypes: [FileSignature.Png, FileSignature.WebP])
            .Produces(StatusCodes.Status404NotFound)
            .WithSummary("Photo de profil de l'utilisateur connecté. Le front ajoute v (date de la photo) pour qu'un changement ne serve pas l'ancienne copie hors ligne.");
        group.MapPut("/", PutAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserAvatar.MaxSize + RequestOverhead))
            .WithBinaryBody(FileSignature.Jpeg, FileSignature.Png, FileSignature.WebP, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .WithSummary("Remplace la photo de profil, envoyée telle quelle (JPEG, PNG ou WebP, 256 Kio au plus).");
        group.MapDelete("/", DeleteAsync)
            .WithSummary("Retire la photo de profil : la pastille reprend l'initiale.");
        return app;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(users.GetUserId(principal), out var userId))
        {
            return TypedResults.Unauthorized();
        }
        var avatar = await db.UserAvatars.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (avatar is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.File(avatar.Content, avatar.ContentType);
    }

    private static async Task<IResult> PutAsync(
        ClaimsPrincipal principal, HttpRequest request, UserManager<AppUser> users, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var content = await ReadBoundedAsync(request.Body, UserAvatar.MaxSize, ct);
        if (content is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Photo trop volumineuse.",
                detail: $"Taille maximale : {UserAvatar.MaxSize / 1024} Kio.");
        }
        if (content.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Le fichier est vide."] });
        }
        var type = FileSignature.Detect(content.AsSpan(0, Math.Min(content.Length, FileSignature.HeaderLength)));
        if (type is not (FileSignature.Jpeg or FileSignature.Png or FileSignature.WebP))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Type de fichier non accepté.",
                detail: "Formats acceptés : JPEG, PNG et WebP.");
        }

        var avatar = await db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (avatar is null)
        {
            avatar = new UserAvatar { UserId = user.Id };
            db.UserAvatars.Add(avatar);
        }
        avatar.Content = content;
        avatar.ContentType = type;
        user.AvatarUpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await CurrentUser.FromAsync(users, user));
    }

    private static async Task<Results<Ok<CurrentUser>, UnauthorizedHttpResult>> DeleteAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }
        await db.UserAvatars.Where(a => a.UserId == user.Id).ExecuteDeleteAsync(ct);
        if (user.AvatarUpdatedAt is not null)
        {
            user.AvatarUpdatedAt = null;
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.Ok(await CurrentUser.FromAsync(users, user));
    }

    /// <summary>Lit tout le corps, ou renvoie null s'il dépasse <paramref name="maxSize"/> octets.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream body, int maxSize, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxSize)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
