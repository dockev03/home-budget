using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Advances;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Advances;

/// <summary>Avances à recevoir (B12) : montant attendu, revenus rattachés, reste à recevoir et statut.</summary>
public static class AdvanceEndpoints
{
    public static IEndpointRouteBuilder MapAdvanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/advances").WithTags("Advances");

        group.MapGet("/", ListAsync)
            .WithSummary("Avances, les ouvertes d'abord, puis de la plus récente à la plus ancienne.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée une avance à recevoir.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<AdvanceDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une avance (If-Match requis), ou la solde à la main.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une avance (If-Match requis). Les revenus rattachés sont conservés, sans lien.");

        return app;
    }

    public enum AdvanceStatus
    {
        Open,
        Settled,
    }

    /// <param name="IsClosed">Soldée à la main, même si le reçu n'atteint pas le montant attendu.</param>
    public sealed record AdvanceRequest(
        [property: Required, StringLength(Advance.MaxLabelLength, MinimumLength = 1)] string Label,
        [property: Range(0.01, 999_999_999)] decimal ExpectedAmount,
        DateOnly Date,
        [property: StringLength(2000)] string? Notes,
        bool IsClosed);

    /// <param name="Received">Total des revenus rattachés.</param>
    /// <param name="Remaining">Reste à recevoir (0 si soldée).</param>
    public sealed record AdvanceDto(
        Guid Id,
        string Label,
        decimal ExpectedAmount,
        DateOnly Date,
        string? Notes,
        bool IsClosed,
        decimal Received,
        decimal Remaining,
        AdvanceStatus Status,
        int TransactionCount,
        int Version);

    private static async Task<Ok<List<AdvanceDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var advances = await db.Advances.AsNoTracking().ToListAsync(ct);
        var dtos = await ToDtosAsync(db, advances, ct);
        return TypedResults.Ok(dtos.OrderBy(a => a.Status).ThenByDescending(a => a.Date).ThenBy(a => a.Label).ToList());
    }

    private static async Task<Results<Created<AdvanceDto>, ValidationProblem>> CreateAsync(AdvanceRequest request, AppDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }
        var advance = new Advance { Label = request.Label.Trim() };
        Apply(advance, request);
        db.Advances.Add(advance);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/advances/{advance.Id}", (await ToDtosAsync(db, [advance], ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(Guid id, AdvanceRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var advance = await db.Advances.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, advance) is { } precondition)
        {
            return precondition;
        }
        if (Validate(request) is { } problem)
        {
            return problem;
        }
        Apply(advance, request);
        if (!await db.TrySaveAsync(ct))
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok((await ToDtosAsync(db, [advance], ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var advance = await db.Advances.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (advance is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, advance) is { } precondition)
        {
            return precondition;
        }
        db.Advances.Remove(advance);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static ValidationProblem? Validate(AdvanceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!TransactionValidation.HasCents(request.ExpectedAmount))
        {
            errors["expectedAmount"] = ["Le montant a au plus 2 décimales."];
        }
        if (request.Date < TransactionValidation.MinDate || request.Date > TransactionValidation.MaxDate)
        {
            errors["date"] = ["Date invalide."];
        }
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            errors["label"] = ["Le libellé est obligatoire."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void Apply(Advance advance, AdvanceRequest request)
    {
        advance.Label = request.Label.Trim();
        advance.ExpectedAmount = request.ExpectedAmount;
        advance.Date = request.Date;
        advance.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        advance.IsClosed = request.IsClosed;
    }

    private static async Task<List<AdvanceDto>> ToDtosAsync(AppDbContext db, IReadOnlyList<Advance> advances, CancellationToken ct)
    {
        var ids = advances.Select(a => a.Id).ToList();
        var received = (await db.Transactions.AsNoTracking()
                .Where(t => t.AdvanceId != null && ids.Contains(t.AdvanceId.Value))
                .Select(t => new { AdvanceId = t.AdvanceId!.Value, t.Amount })
                .ToListAsync(ct))
            .GroupBy(x => x.AdvanceId)
            .ToDictionary(g => g.Key, g => (Sum: g.Sum(x => x.Amount), Count: g.Count()));

        return [.. advances.Select(a =>
        {
            var (sum, count) = received.GetValueOrDefault(a.Id);
            var settled = Advance.IsSettled(a.ExpectedAmount, sum, a.IsClosed);
            return new AdvanceDto(
                a.Id, a.Label, a.ExpectedAmount, a.Date, a.Notes, a.IsClosed, sum,
                settled ? 0 : a.ExpectedAmount - sum,
                settled ? AdvanceStatus.Settled : AdvanceStatus.Open,
                count,
                a.Version);
        })];
    }
}
