using System.Globalization;
using System.IO.Compression;

using HomeBudget.Api.Domain.Attachments;
using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Features.Attachments;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Backups;

/// <param name="Imported">Fichiers ajoutés.</param>
/// <param name="Skipped">Fichiers déjà présents.</param>
/// <param name="Rejected">Entrées refusées : nom, taille, type ou empreinte invalides.</param>
/// <param name="Missing">Pièces jointes dont le fichier manque encore après l'import.</param>
public sealed record AttachmentArchiveResultDto(int Imported, int Skipped, int Rejected, int Missing);

/// <summary>
/// Archive ZIP des fichiers de pièces jointes (H6, B7), complément de la sauvegarde JSON : un fichier par empreinte
/// SHA-256. L'import ajoute les fichiers manquants, sans rien remplacer (le contenu d'un nom est fixé par son empreinte).
/// </summary>
public static class BackupAttachmentEndpoints
{
    /// <summary>Taille maximale d'une archive importée (2 Gio).</summary>
    public const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024;

    public static RouteGroupBuilder MapAttachmentArchiveEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/attachments", ExportAsync)
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .WithSummary("Télécharge l'archive ZIP des fichiers de pièces jointes (nommés par empreinte SHA-256).");
        group.MapPost("/attachments", ImportAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxArchiveBytes))
            .WithBinaryBody("application/zip")
            .Produces<AttachmentArchiveResultDto>()
            .ProducesValidationProblem()
            .WithSummary("Importe une archive de pièces jointes envoyée telle quelle : chaque fichier est vérifié (empreinte, taille, type) et ajouté s'il manque.");
        return group;
    }

    private static async Task<IResult> ExportAsync(
        AppDbContext db, AttachmentStore store, AuditTrail audit, TimeProvider clock, CancellationToken ct)
    {
        db.IgnoreVisibility = true;
        var hashes = await db.Attachments.AsNoTracking().Select(a => a.Sha256).Distinct().OrderBy(h => h).ToListAsync(ct);
        var temp = store.NewTempFile();
        var count = 0;
        await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
        await using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            foreach (var hash in hashes.Where(store.Exists))
            {
                // Photos et PDF sont déjà compressés : rangés tels quels.
                var entry = zip.CreateEntry(hash, CompressionLevel.NoCompression);
                await using var output = await entry.OpenAsync(ct);
                await using var input = store.OpenRead(hash);
                await input.CopyToAsync(output, ct);
                count++;
            }
        }
        await audit.RecordAsync(AuditAction.BackupExported, details: $"Archive des pièces jointes : {count} fichier(s).");
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        return TypedResults.File(stream, "application/zip", $"home-budget-pieces-jointes-{stamp}.zip");
    }

    private static async Task<IResult> ImportAsync(
        HttpRequest request, AppDbContext db, AttachmentStore store, AuditTrail audit, CancellationToken ct)
    {
        var temp = store.NewTempFile();
        try
        {
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                await request.Body.CopyToAsync(file, ct);
            }
            int imported = 0, skipped = 0, rejected = 0;
            try
            {
                await using var input = new FileStream(temp, FileMode.Open, FileAccess.Read);
                await using var zip = new ZipArchive(input, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/'))
                    {
                        continue;
                    }
                    if (!AttachmentStore.IsValidHash(entry.FullName) || entry.Length is <= 0 or > Attachment.MaxSize)
                    {
                        rejected++;
                        continue;
                    }
                    if (store.Exists(entry.FullName))
                    {
                        skipped++;
                        continue;
                    }
                    await using var content = await entry.OpenAsync(ct);
                    var (stored, _) = await store.SaveAsync(content, ct);
                    if (stored?.Sha256 == entry.FullName)
                    {
                        imported++;
                    }
                    else
                    {
                        // Contenu qui ne correspond pas à son nom : il sera effacé comme orphelin.
                        rejected++;
                    }
                }
            }
            catch (InvalidDataException)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Archive ZIP illisible."] });
            }

            db.IgnoreVisibility = true;
            var hashes = await db.Attachments.AsNoTracking().Select(a => a.Sha256).Distinct().ToListAsync(ct);
            var missing = hashes.Count(h => !store.Exists(h));
            await audit.RecordAsync(AuditAction.AttachmentsRestored,
                details: $"{imported} fichier(s) ajouté(s), {skipped} déjà présent(s), {rejected} refusé(s), {missing} manquant(s).");
            return TypedResults.Ok(new AttachmentArchiveResultDto(imported, skipped, rejected, missing));
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
