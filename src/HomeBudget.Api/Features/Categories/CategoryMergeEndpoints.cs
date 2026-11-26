using System.Security.Claims;

using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Categories;

/// <summary>
/// Fusion de deux catégories (C5) : les lignes d'opération, récurrences, règles apprises, sous-catégories et le budget
/// de la catégorie source passent à la catégorie cible, puis la source est supprimée. Aperçu, puis fusion en une transaction.
/// </summary>
public static partial class CategoryMergeEndpoints
{
    public static IEndpointRouteBuilder MapCategoryMergeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").WithTags("Categories");

        group.MapGet("/{id:guid}/merge-preview", PreviewAsync)
            .Produces<MergeDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Ce que la fusion de la catégorie dans la cible déplacerait. Rien n'est modifié.");
        group.MapPost("/{id:guid}/merge", MergeAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<MergeDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Fusionne la catégorie dans la cible (If-Match requis : version de la catégorie source), puis la supprime.");

        return app;
    }

    public sealed record MergeRequest(Guid TargetId);

    /// <param name="Lines">Lignes d'opération déplacées.</param>
    /// <param name="Recurrences">Récurrences déplacées.</param>
    /// <param name="Rules">Règles apprises déplacées.</param>
    /// <param name="Subcategories">Sous-catégories rattachées à la cible.</param>
    /// <param name="BudgetMoved">Le budget de la source devient celui de la cible (qui n'en avait pas).</param>
    /// <param name="BudgetDropped">Le budget de la source est supprimé : la cible garde le sien.</param>
    public sealed record MergeDto(
        Guid SourceId, string SourceName, Guid TargetId, string TargetName,
        int Lines, int Recurrences, int Rules, int Subcategories, bool BudgetMoved, bool BudgetDropped);

    private static async Task<IResult> PreviewAsync(Guid id, Guid targetId, AppDbContext db, CancellationToken ct)
    {
        var source = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (source is null)
        {
            return TypedResults.NotFound();
        }
        var (target, problem) = await ValidateAsync(db, source, targetId, ct);
        return problem ?? TypedResults.Ok(await CountAsync(db, source, target!, ct));
    }

    private static async Task<IResult> MergeAsync(
        Guid id, MergeRequest request, HttpRequest http, AppDbContext db, ClaimsPrincipal user, ILoggerFactory loggers, CancellationToken ct)
    {
        var source = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (source is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, source) is { } precondition)
        {
            return precondition;
        }
        var (target, problem) = await ValidateAsync(db, source, request.TargetId, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await CountAsync(db, source, target!, ct);
        var targetId = target!.Id;
        await db.TransactionLines.Where(l => l.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CategoryId, targetId), ct);
        await db.Recurrences.Where(r => r.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CategoryId, targetId), ct);
        await db.LabelRules.Where(r => r.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CategoryId, targetId), ct);
        await db.Categories.Where(c => c.ParentId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentId, targetId), ct);
        if (result.BudgetMoved)
        {
            await db.CategoryBudgets.Where(b => b.CategoryId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.CategoryId, targetId), ct);
        }
        else if (result.BudgetDropped)
        {
            await db.CategoryBudgets.Where(b => b.CategoryId == id).ExecuteDeleteAsync(ct);
        }
        db.Categories.Remove(source);
        if (!await db.TrySaveAsync(ct))
        {
            return Concurrency.Conflict();
        }
        await transaction.CommitAsync(ct);

        LogMerge(loggers.CreateLogger("HomeBudget.Categories"), user.Identity?.Name, result.SourceName, result.TargetName, result.Lines, result.Recurrences);
        return TypedResults.Ok(result);
    }

    private static async Task<(Category? Target, IResult? Problem)> ValidateAsync(
        AppDbContext db, Category source, Guid targetId, CancellationToken ct)
    {
        string? error = null;
        var target = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == targetId, ct);
        if (target is null)
        {
            error = "Catégorie cible introuvable.";
        }
        else if (target.Id == source.Id)
        {
            error = "Choisissez une autre catégorie que celle à fusionner.";
        }
        else if (target.Kind != source.Kind)
        {
            error = "Les deux catégories doivent être du même type.";
        }
        else if (target.ParentId == source.Id)
        {
            error = "Une catégorie ne peut pas être fusionnée dans l'une de ses sous-catégories.";
        }
        else if (target.ParentId is not null && await db.Categories.AnyAsync(c => c.ParentId == source.Id, ct))
        {
            error = "Cette catégorie a des sous-catégories : choisissez une catégorie principale comme cible.";
        }
        return error is null
            ? (target, null)
            : (null, TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["targetId"] = [error] }));
    }

    private static async Task<MergeDto> CountAsync(AppDbContext db, Category source, Category target, CancellationToken ct)
    {
        var sourceBudget = await db.CategoryBudgets.AnyAsync(b => b.CategoryId == source.Id, ct);
        var targetBudget = await db.CategoryBudgets.AnyAsync(b => b.CategoryId == target.Id, ct);
        return new MergeDto(
            source.Id,
            source.Name,
            target.Id,
            target.Name,
            await db.TransactionLines.CountAsync(l => l.CategoryId == source.Id, ct),
            await db.Recurrences.CountAsync(r => r.CategoryId == source.Id, ct),
            await db.LabelRules.CountAsync(r => r.CategoryId == source.Id, ct),
            await db.Categories.CountAsync(c => c.ParentId == source.Id, ct),
            sourceBudget && !targetBudget,
            sourceBudget && targetBudget);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catégorie « {Source} » fusionnée dans « {Target} » par {User} : {Lines} lignes, {Recurrences} récurrences.")]
    private static partial void LogMerge(ILogger logger, string? user, string source, string target, int lines, int recurrences);
}
