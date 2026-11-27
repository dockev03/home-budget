using System.Security.Claims;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Account;

/// <summary>
/// Historique des connexions de l'utilisateur connecté, lu dans le journal d'audit (N6) : connexions réussies,
/// tentatives échouées et verrouillages. Chaque utilisateur, lecture seule compris, ne voit que les siennes.
/// </summary>
public static class LoginHistoryEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapLoginHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/account/logins", ListAsync).WithTags("Account")
            .WithSummary("Historique des connexions (date, adresse IP, navigateur, résultat), la plus récente en premier.");
        return app;
    }

    /// <param name="Action">LoginSucceeded, LoginFailed ou LoginLockedOut.</param>
    /// <param name="Details">Méthode de connexion ou motif de l'échec.</param>
    /// <param name="Browser">Navigateur lu dans l'en-tête User-Agent ; absent pour les connexions plus anciennes que son enregistrement.</param>
    /// <param name="OperatingSystem">Système d'exploitation lu dans l'en-tête User-Agent.</param>
    /// <param name="Device">Type d'appareil lu dans l'en-tête User-Agent.</param>
    public sealed record LoginEntryDto(
        Guid Id,
        DateTimeOffset At,
        AuditAction Action,
        string? Details,
        string? IpAddress,
        string? Browser,
        string? OperatingSystem,
        DeviceKind Device);

    /// <param name="RetentionDays">Durée de conservation configurée, en jours ; absente : conservation illimitée.</param>
    public sealed record LoginHistoryPage(IReadOnlyList<LoginEntryDto> Items, int Total, int Page, int PageSize, int? RetentionDays);

    private static async Task<Results<Ok<LoginHistoryPage>, ValidationProblem>> ListAsync(
        HttpContext http, AppDbContext db, IOptions<HomeBudgetOptions> options,
        int? page, int? pageSize, CancellationToken ct)
    {
        var size = pageSize ?? 20;
        var number = page ?? 1;
        if (size is < 1 or > MaxPageSize || number < 1)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"Page à partir de 1, de 1 à {MaxPageSize} entrées."],
            });
        }

        var userId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var query = db.AuditLogs.AsNoTracking().Where(l => l.UserId == userId && AuditLog.LoginActions.Contains(l.Action));
        var total = await query.CountAsync(ct);
        // Identifiants UUID v7 : l'ordre des identifiants est l'ordre chronologique.
        var items = await query.OrderByDescending(l => l.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        var retention = options.Value.Security.LoginHistoryRetentionDays;
        return TypedResults.Ok(new LoginHistoryPage(items.ConvertAll(ToDto), total, number, size, retention > 0 ? retention : null));
    }

    private static LoginEntryDto ToDto(AuditLog l)
    {
        var agent = l.UserAgent is null ? null : UserAgentInfo.Parse(l.UserAgent);
        return new LoginEntryDto(l.Id, l.At, l.Action, l.Details, l.IpAddress, agent?.Browser, agent?.OperatingSystem, agent?.Device ?? DeviceKind.Unknown);
    }
}
