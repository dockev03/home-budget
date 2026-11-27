using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using HomeBudget.Api.Domain.Attachments;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Attachments;

public enum StoreError
{
    Empty,
    TooLarge,
    Unsupported,
}

/// <summary>Fichier enregistré : empreinte, taille et type reconnu.</summary>
public sealed record StoredFile(string Sha256, long Size, string ContentType);

/// <summary>
/// Stockage des pièces jointes (B7) dans <c>/data/attachments</c> : un fichier par empreinte SHA-256, écrit une fois
/// et jamais modifié. Le contenu est d'abord écrit dans un fichier temporaire, en calculant l'empreinte et en vérifiant
/// la taille et le type, puis déplacé sous son nom définitif.
/// </summary>
public sealed partial class AttachmentStore(IOptions<HomeBudgetOptions> options, TimeProvider clock)
{
    /// <summary>Délai avant qu'un fichier sans pièce jointe soit effacé (envoi ou restauration en cours).</summary>
    public static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(24);

    private string Root => Directory.CreateDirectory(options.Value.AttachmentsPath).FullName;

    private string TempRoot => Directory.CreateDirectory(Path.Combine(Root, ".tmp")).FullName;

    public static bool IsValidHash(string? value) => value is not null && HashPattern().IsMatch(value);

    public string PathOf(string sha256) =>
        IsValidHash(sha256) ? Path.Combine(Root, sha256) : throw new ArgumentException("Empreinte invalide.", nameof(sha256));

    public bool Exists(string sha256) => File.Exists(PathOf(sha256));

    /// <summary>Enregistre un contenu, en refusant au-delà de <paramref name="maxSize"/> octets ou d'un type non accepté.</summary>
    public async Task<(StoredFile? File, StoreError? Error)> SaveAsync(Stream content, CancellationToken ct, long maxSize = Attachment.MaxSize)
    {
        var temp = Path.Combine(TempRoot, $"{Guid.NewGuid():N}");
        var header = new byte[FileSignature.HeaderLength];
        var headerLength = 0;
        long size = 0;
        string sha;
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    size += read;
                    if (size > maxSize)
                    {
                        return (null, StoreError.TooLarge);
                    }
                    if (headerLength < header.Length)
                    {
                        var take = Math.Min(read, header.Length - headerLength);
                        buffer.AsSpan(0, take).CopyTo(header.AsSpan(headerLength));
                        headerLength += take;
                    }
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            if (size == 0)
            {
                return (null, StoreError.Empty);
            }
            var type = FileSignature.Detect(header.AsSpan(0, headerLength));
            if (type is null)
            {
                return (null, StoreError.Unsupported);
            }
            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            var target = PathOf(sha);
            if (File.Exists(target))
            {
                // Même contenu déjà stocké : rafraîchi pour ne pas être pris pour un orphelin pendant l'enregistrement.
                File.SetLastWriteTimeUtc(target, clock.GetUtcNow().UtcDateTime);
            }
            else
            {
                try
                {
                    File.Move(temp, target);
                }
                catch (IOException) when (File.Exists(target))
                {
                    // Envoi simultané du même fichier : l'autre copie est identique.
                }
            }
            return (new StoredFile(sha, size, type), null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Chemin d'un fichier temporaire dans le volume de données, et non dans /tmp (en mémoire dans le conteneur) :
    /// les archives de pièces jointes peuvent être volumineuses.
    /// </summary>
    public string NewTempFile() => Path.Combine(TempRoot, $"{Guid.NewGuid():N}");

    public FileStream OpenRead(string sha256) =>
        new(PathOf(sha256), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    /// <summary>Efface le fichier si plus aucune pièce jointe ne l'utilise, toutes visibilités confondues.</summary>
    public async Task DeleteIfUnusedAsync(AppDbContext db, string sha256, CancellationToken ct)
    {
        if (!await db.Attachments.IgnoreQueryFilters().AnyAsync(a => a.Sha256 == sha256, ct) && Exists(sha256))
        {
            File.Delete(PathOf(sha256));
        }
    }

    /// <summary>
    /// Efface les fichiers qu'aucune pièce jointe n'utilise (opérations supprimées, restauration) et les fichiers
    /// temporaires abandonnés, s'ils datent de plus de <see cref="OrphanGrace"/>.
    /// </summary>
    public async Task<int> PurgeOrphansAsync(AppDbContext db, CancellationToken ct)
    {
        var limit = clock.GetUtcNow().UtcDateTime - OrphanGrace;
        var used = (await db.Attachments.IgnoreQueryFilters().Select(a => a.Sha256).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var purged = 0;
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles())
        {
            if (IsValidHash(file.Name) && !used.Contains(file.Name) && file.LastWriteTimeUtc < limit)
            {
                file.Delete();
                purged++;
            }
        }
        foreach (var file in new DirectoryInfo(TempRoot).EnumerateFiles().Where(f => f.LastWriteTimeUtc < limit))
        {
            file.Delete();
        }
        return purged;
    }

    /// <summary>Nom de fichier affichable : sans chemin ni caractère de contrôle, et de longueur bornée.</summary>
    public static string CleanFileName(string? name, string contentType)
    {
        var clean = new string(Path.GetFileName((name ?? string.Empty).Replace('\\', '/')).Where(c => !char.IsControl(c) && c is not ('/' or '\\')).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "piece-jointe" + contentType switch
            {
                FileSignature.Jpeg => ".jpg",
                FileSignature.Png => ".png",
                FileSignature.WebP => ".webp",
                FileSignature.Heic => ".heic",
                _ => ".pdf",
            };
        }
        return clean.Length <= Attachment.MaxFileNameLength ? clean : clean[^Attachment.MaxFileNameLength..];
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();
}
