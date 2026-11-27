using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using HomeBudget.Api.Domain.Dashboard;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Dashboard;

/// <summary>
/// Disposition de l'accueil de l'utilisateur connecté : ordre des blocs et blocs masqués. Chaque utilisateur, lecture
/// seule compris, ne voit et ne modifie que la sienne.
/// </summary>
public static class DashboardLayoutEndpoints
{
    public static IEndpointRouteBuilder MapDashboardLayoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard/layout").WithTags("Dashboard");

        group.MapGet("/", GetAsync)
            .WithSummary("Disposition de l'accueil : blocs dans l'ordre d'affichage et blocs masqués.");
        group.MapPut("/", SetAsync)
            .Produces<LayoutDto>().ProducesValidationProblem()
            .WithSummary("Enregistre l'ordre des blocs de l'accueil et les blocs masqués ; les blocs inconnus sont ignorés, les blocs absents ajoutés à la fin.");
        group.MapDelete("/", ResetAsync)
            .Produces<LayoutDto>()
            .WithSummary("Rétablit la disposition par défaut de l'accueil.");
        return app;
    }

    /// <param name="Order">Tous les blocs, dans l'ordre d'affichage.</param>
    /// <param name="Hidden">Blocs masqués, dans l'ordre d'affichage.</param>
    /// <param name="IsDefault">Aucune disposition enregistrée : celle par défaut s'applique.</param>
    public sealed record LayoutDto(IReadOnlyList<string> Order, IReadOnlyList<string> Hidden, bool IsDefault);

    public sealed record LayoutRequest(
        [property: Required, MaxLength(DashboardLayout.MaxBlocks)] List<string> Order,
        [property: MaxLength(DashboardLayout.MaxBlocks)] List<string>? Hidden);

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static LayoutDto ToDto(DashboardLayout? layout)
    {
        var (order, hidden) = DashboardLayout.Normalize(layout?.Order, layout?.Hidden);
        return new LayoutDto(order, hidden, layout is null);
    }

    private static async Task<Ok<LayoutDto>> GetAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = UserId(principal);
        var layout = await db.DashboardLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);
        return TypedResults.Ok(ToDto(layout));
    }

    private static async Task<IResult> SetAsync(LayoutRequest request, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (request.Order.Concat(request.Hidden ?? []).Any(b => b is null || b.Length > DashboardLayout.MaxBlockLength))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["order"] = ["Identifiant de bloc invalide."] });
        }

        var userId = UserId(principal);
        var layout = await db.DashboardLayouts.FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (layout is null)
        {
            layout = new DashboardLayout { UserId = userId };
            db.DashboardLayouts.Add(layout);
        }
        (layout.Order, layout.Hidden) = DashboardLayout.Normalize(request.Order, request.Hidden);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(layout));
    }

    private static async Task<Ok<LayoutDto>> ResetAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = UserId(principal);
        await db.DashboardLayouts.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        return TypedResults.Ok(ToDto(null));
    }
}
