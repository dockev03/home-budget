using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Audit;

/// <summary>Consultation du journal d'audit (N6), réservée aux administrateurs.</summary>
public static class AuditEndpoints
{
    public const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", ListAsync).WithTags("Audit").RequireAuthorization(AuthorizationPolicies.Admin)
            .WithSummary("Journal d'audit, du plus récent au plus ancien, filtrable par action et par utilisateur.");
        return app;
    }

    public sealed record AuditEntryDto(
        Guid Id,
        DateTimeOffset At,
        Guid? UserId,
        string? UserName,
        AuditAction Action,
        string? Target,
        Guid? TargetId,
        string? Details,
        string? IpAddress);

    public sealed record AuditPage(IReadOnlyList<AuditEntryDto> Items, int Total, int Page, int PageSize);

    private static async Task<Results<Ok<AuditPage>, ValidationProblem>> ListAsync(
        AppDbContext db, int? page, int? pageSize, AuditAction? action, Guid? userId, CancellationToken ct)
    {
        var size = pageSize ?? 50;
        var number = page ?? 1;
        if (size is < 1 or > MaxPageSize || number < 1)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"Page à partir de 1, de 1 à {MaxPageSize} entrées."],
            });
        }

        var query = db.AuditLogs.AsNoTracking();
        if (action is not null)
        {
            query = query.Where(l => l.Action == action);
        }
        if (userId is not null)
        {
            query = query.Where(l => l.UserId == userId);
        }
        var total = await query.CountAsync(ct);
        // Identifiants UUID v7 : l'ordre des identifiants est l'ordre chronologique.
        var items = await query.OrderByDescending(l => l.Id).Skip((number - 1) * size).Take(size)
            .Select(l => new AuditEntryDto(l.Id, l.At, l.UserId, l.UserName, l.Action, l.Target, l.TargetId, l.Details, l.IpAddress))
            .ToListAsync(ct);
        return TypedResults.Ok(new AuditPage(items, total, number, size));
    }
}
