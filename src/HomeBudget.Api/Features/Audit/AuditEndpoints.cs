using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Security;

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

    /// <param name="Browser">Navigateur lu dans l'en-tête User-Agent ; absent pour les entrées qui ne l'ont pas enregistré.</param>
    /// <param name="OperatingSystem">Système d'exploitation lu dans l'en-tête User-Agent.</param>
    /// <param name="Device">Type d'appareil lu dans l'en-tête User-Agent.</param>
    public sealed record AuditEntryDto(
        Guid Id,
        DateTimeOffset At,
        Guid? UserId,
        string? UserName,
        AuditAction Action,
        string? Target,
        Guid? TargetId,
        string? Details,
        string? IpAddress,
        string? Browser,
        string? OperatingSystem,
        DeviceKind Device);

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
        var items = await query.OrderByDescending(l => l.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return TypedResults.Ok(new AuditPage(items.ConvertAll(ToDto), total, number, size));
    }

    private static AuditEntryDto ToDto(AuditLog l)
    {
        var agent = l.UserAgent is null ? null : UserAgentInfo.Parse(l.UserAgent);
        return new AuditEntryDto(
            l.Id, l.At, l.UserId, l.UserName, l.Action, l.Target, l.TargetId, l.Details, l.IpAddress, agent?.Browser, agent?.OperatingSystem, agent?.Device ?? DeviceKind.Unknown);
    }
}
