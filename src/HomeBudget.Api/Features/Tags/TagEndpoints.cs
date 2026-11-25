using HomeBudget.Api.Domain.Tags;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Tags;

/// <summary>Tags libres (C4) : liste et autocomplétion. Les tags sont créés à la volée par la saisie d'opération.</summary>
public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tags", ListAsync).WithTags("Tags")
            .WithSummary("Tags posés sur au moins une ligne d'opération, les plus utilisés en premier.");
        return app;
    }

    public sealed record TagDto(Guid Id, string Name, int UsageCount);

    private static async Task<Ok<List<TagDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var tags = await db.Tags.AsNoTracking()
            .Select(t => new { t.Id, t.Name, UsageCount = db.TransactionLines.Count(l => l.Tags.Any(g => g.Id == t.Id)) })
            .Where(t => t.UsageCount > 0)
            .OrderByDescending(t => t.UsageCount).ThenBy(t => t.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(tags.ConvertAll(t => new TagDto(t.Id, t.Name, t.UsageCount)));
    }

    /// <summary>Message d'erreur si la liste de tags d'une ligne est invalide, sinon null.</summary>
    public static string? Validate(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return null;
        }
        if (names.Count > Tag.MaxPerLine)
        {
            return $"{Tag.MaxPerLine} tags au plus par ligne.";
        }
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Un tag ne peut pas être vide.";
            }
            if (Tag.Clean(name).Length > Tag.MaxNameLength)
            {
                return $"Un tag fait {Tag.MaxNameLength} caractères au plus.";
            }
        }
        return null;
    }

    /// <summary>Tags existants de même nom (casse ignorée), ou nouveaux tags ajoutés au contexte. Sans doublon.</summary>
    public static async Task<List<Tag>> FindOrCreateAsync(AppDbContext db, IEnumerable<string>? names, CancellationToken ct = default)
    {
        var result = new List<Tag>();
        foreach (var name in names ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            var normalized = Tag.Normalize(name);
            if (result.Exists(t => t.NormalizedName == normalized))
            {
                continue;
            }
            var tag = db.Tags.Local.FirstOrDefault(t => t.NormalizedName == normalized)
                ?? await db.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalized, ct);
            if (tag is null)
            {
                tag = new Tag { Name = Tag.Clean(name), NormalizedName = normalized };
                db.Tags.Add(tag);
            }
            result.Add(tag);
        }
        return result;
    }
}
