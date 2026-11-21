using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Categories;

/// <summary>Catégories sur deux niveaux, typées (C1, C2, C3).</summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").WithTags("Categories");

        group.MapGet("/", ListAsync)
            .WithSummary("Catégories (liste à plat, triée : parents puis enfants).");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée une catégorie ou une sous-catégorie.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<CategoryDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une catégorie (If-Match requis). Le type et l'archivage s'appliquent aussi aux sous-catégories.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une catégorie inutilisée et sans sous-catégorie (If-Match requis) ; sinon, l'archiver.");

        return app;
    }

    public sealed record CategoryRequest(
        [property: Required, StringLength(60, MinimumLength = 1)] string Name,
        CategoryKind Kind,
        Guid? ParentId,
        [property: StringLength(16)] string? Color,
        [property: StringLength(40)] string? Icon,
        int? SortOrder,
        bool? IsArchived);

    public sealed record CategoryDto(
        Guid Id, Guid? ParentId, string Name, CategoryKind Kind, string? Color, string? Icon, int SortOrder, bool IsArchived, int Version);

    private static async Task<Ok<List<CategoryDto>>> ListAsync(AppDbContext db, bool? includeArchived)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => includeArchived == true || !c.IsArchived)
            .ToListAsync();
        var parents = categories.Where(c => c.ParentId is null).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name);
        var ordered = new List<CategoryDto>();
        foreach (var parent in parents)
        {
            ordered.Add(ToDto(parent));
            ordered.AddRange(categories.Where(c => c.ParentId == parent.Id).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(ToDto));
        }
        return TypedResults.Ok(ordered);
    }

    private static async Task<Results<Created<CategoryDto>, ValidationProblem>> CreateAsync(CategoryRequest request, AppDbContext db)
    {
        var (parent, problem) = await ValidateAsync(request, db, null);
        if (problem is not null)
        {
            return problem;
        }
        var category = new Category
        {
            Name = request.Name.Trim(),
            ParentId = parent?.Id,
            Kind = parent?.Kind ?? request.Kind,
            Color = Clean(request.Color),
            Icon = Clean(request.Icon),
            IsArchived = request.IsArchived ?? false,
            SortOrder = request.SortOrder
                ?? (await db.Categories.Where(c => c.ParentId == request.ParentId).MaxAsync(c => (int?)c.SortOrder) ?? -1) + 1,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/categories/{category.Id}", ToDto(category));
    }

    private static async Task<IResult> UpdateAsync(Guid id, CategoryRequest request, HttpRequest http, AppDbContext db)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, category) is { } precondition)
        {
            return precondition;
        }
        var (parent, problem) = await ValidateAsync(request, db, category);
        if (problem is not null)
        {
            return problem;
        }

        category.Name = request.Name.Trim();
        category.ParentId = parent?.Id;
        category.Kind = parent?.Kind ?? request.Kind;
        category.Color = Clean(request.Color);
        category.Icon = Clean(request.Icon);
        category.SortOrder = request.SortOrder ?? category.SortOrder;
        category.IsArchived = request.IsArchived ?? category.IsArchived;

        // Une sous-catégorie a toujours le type de son parent ; l'archivage du parent masque ses enfants.
        foreach (var child in await db.Categories.Where(c => c.ParentId == id).ToListAsync())
        {
            child.Kind = category.Kind;
            if (category.IsArchived)
            {
                child.IsArchived = true;
            }
        }

        return await db.TrySaveAsync() ? TypedResults.Ok(ToDto(category)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, category) is { } precondition)
        {
            return precondition;
        }
        if (await db.Categories.AnyAsync(c => c.ParentId == id) || await db.TransactionLines.AnyAsync(l => l.CategoryId == id))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cette catégorie est utilisée.",
                detail: "Elle a des sous-catégories ou des opérations : archivez-la plutôt.");
        }
        db.Categories.Remove(category);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<(Category? Parent, ValidationProblem? Problem)> ValidateAsync(
        CategoryRequest request, AppDbContext db, Category? existing)
    {
        var errors = new Dictionary<string, string[]>();
        Category? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId);
            if (parent is null)
            {
                errors["parentId"] = ["Catégorie parente introuvable."];
            }
            else if (parent.ParentId is not null || parent.Id == existing?.Id)
            {
                errors["parentId"] = ["Deux niveaux au maximum : choisissez une catégorie principale."];
            }
            else if (existing is not null && await db.Categories.AnyAsync(c => c.ParentId == existing.Id))
            {
                errors["parentId"] = ["Cette catégorie a des sous-catégories : elle ne peut pas devenir une sous-catégorie."];
            }
        }

        var name = request.Name.Trim();
        var siblings = await db.Categories
            .Where(c => c.ParentId == request.ParentId && (existing == null || c.Id != existing.Id))
            .Select(c => c.Name)
            .ToListAsync();
        if (siblings.Any(n => string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            errors["name"] = ["Une catégorie porte déjà ce nom à ce niveau."];
        }
        return (parent, errors.Count == 0 ? null : TypedResults.ValidationProblem(errors));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CategoryDto ToDto(Category c) =>
        new(c.Id, c.ParentId, c.Name, c.Kind, c.Color, c.Icon, c.SortOrder, c.IsArchived, c.Version);
}
