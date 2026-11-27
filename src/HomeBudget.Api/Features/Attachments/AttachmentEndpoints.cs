using HomeBudget.Api.Domain.Attachments;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace HomeBudget.Api.Features.Attachments;

/// <param name="CreatedBy">Utilisateur qui a ajouté la pièce jointe.</param>
public sealed record AttachmentDto(
    Guid Id, Guid TransactionId, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt, Guid? CreatedBy);

/// <summary>
/// Pièces jointes des opérations (B7) : photo d'un ticket, facture, fiche de paie attachée à l'opération de salaire.
/// Le fichier est envoyé tel quel dans le corps de la requête (pas de formulaire multipart), son nom en paramètre.
/// </summary>
public static class AttachmentEndpoints
{
    /// <summary>Marge de la requête au-delà de la taille du fichier.</summary>
    private const long RequestOverhead = 64 * 1024;

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var transactions = app.MapGroup("/api/transactions/{transactionId:guid}/attachments").WithTags("Attachments");
        transactions.MapGet("/", ListAsync)
            .WithSummary("Pièces jointes d'une opération, de la plus ancienne à la plus récente.");
        transactions.MapPost("/", UploadAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithMetadata(new RequestSizeLimitAttribute(Attachment.MaxSize + RequestOverhead))
            .WithBinaryBody(FileSignature.Jpeg, FileSignature.Png, FileSignature.WebP, FileSignature.Heic, FileSignature.Pdf, "application/octet-stream")
            .Produces<AttachmentDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .WithSummary("Joint un fichier (JPEG, PNG, WebP, HEIC ou PDF, 10 Mio au plus) envoyé tel quel dans le corps de la requête.");

        var group = app.MapGroup("/api/attachments").WithTags("Attachments");
        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Description d'une pièce jointe.");
        group.MapGet("/{id:guid}/content", ContentAsync)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Contenu du fichier. Les images s'affichent dans le navigateur, sauf avec download=true ; le PDF et le HEIC se téléchargent.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retire une pièce jointe de son opération. Le fichier est effacé quand plus aucune opération ne l'utilise.");
        return app;
    }

    private static async Task<Results<Ok<List<AttachmentDto>>, NotFound>> ListAsync(Guid transactionId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Transactions.AnyAsync(t => t.Id == transactionId, ct))
        {
            return TypedResults.NotFound();
        }
        var items = await db.Attachments.AsNoTracking()
            .Where(a => a.TransactionId == transactionId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(items.ConvertAll(ToDto));
    }

    private static async Task<IResult> UploadAsync(
        Guid transactionId, string? fileName, HttpRequest request, AppDbContext db, AttachmentStore store, CancellationToken ct)
    {
        if (!await db.Transactions.AnyAsync(t => t.Id == transactionId, ct))
        {
            return TypedResults.NotFound();
        }
        if (await db.Attachments.CountAsync(a => a.TransactionId == transactionId, ct) >= Attachment.MaxPerTransaction)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"Une opération a au plus {Attachment.MaxPerTransaction} pièces jointes."],
            });
        }

        var (file, error) = await store.SaveAsync(request.Body, ct);
        switch (error)
        {
            case StoreError.Empty:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Le fichier est vide."] });
            case StoreError.TooLarge:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Fichier trop volumineux.",
                    detail: $"Taille maximale : {Attachment.MaxSize / (1024 * 1024)} Mio.");
            case StoreError.Unsupported:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Type de fichier non accepté.",
                    detail: "Formats acceptés : JPEG, PNG, WebP, HEIC et PDF.");
        }

        var attachment = new Attachment
        {
            TransactionId = transactionId,
            FileName = AttachmentStore.CleanFileName(fileName, file!.ContentType),
            ContentType = file.ContentType,
            Size = file.Size,
            Sha256 = file.Sha256,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/attachments/{attachment.Id}", ToDto(attachment));
    }

    private static async Task<Results<Ok<AttachmentDto>, NotFound>> GetAsync(Guid id, AppDbContext db, CancellationToken ct) =>
        await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is { } attachment
            ? TypedResults.Ok(ToDto(attachment))
            : TypedResults.NotFound();

    private static async Task<IResult> ContentAsync(
        Guid id, bool? download, HttpContext http, AppDbContext db, AttachmentStore store, CancellationToken ct)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null || !store.Exists(attachment.Sha256))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Fichier introuvable.");
        }
        var inline = download != true && FileSignature.IsInline(attachment.ContentType);
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(attachment.FileName);
        http.Response.Headers.ContentDisposition = disposition.ToString();
        return TypedResults.Stream(store.OpenRead(attachment.Sha256), attachment.ContentType);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AttachmentStore store, CancellationToken ct)
    {
        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null)
        {
            return TypedResults.NotFound();
        }
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        await store.DeleteIfUnusedAsync(db, attachment.Sha256, ct);
        return TypedResults.NoContent();
    }

    private static AttachmentDto ToDto(Attachment a) =>
        new(a.Id, a.TransactionId, a.FileName, a.ContentType, a.Size, a.CreatedAt, a.CreatedBy);
}
