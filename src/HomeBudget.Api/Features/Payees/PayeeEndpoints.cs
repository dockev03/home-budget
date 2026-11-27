using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Payees;

/// <summary>Tiers (B8) : autocomplétion. Les tiers sont créés à la volée par la saisie d'opération.</summary>
public static class PayeeEndpoints
{
    public static IEndpointRouteBuilder MapPayeeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payees", SearchAsync).WithTags("Payees")
            .WithSummary("Tiers dont le nom contient le texte recherché, les plus utilisés en premier ; ou les organismes de santé seuls.");
        app.MapPut("/api/payees/{id:guid}/reimburser", SetReimburserAsync).WithTags("Payees")
            .RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<PayeeDto>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .WithSummary("Désigne le tiers comme organisme de santé (CPAM ou mutuelle), ou le retire (P4).");
        return app;
    }

    /// <param name="Reimburser">Organisme de santé que le tiers représente (P4).</param>
    public sealed record PayeeDto(Guid Id, string Name, int UsageCount, Reimburser Reimburser);

    public sealed record ReimburserRequest(Reimburser Reimburser);

    /// <param name="reimbursers">Seulement les organismes de santé (CPAM, mutuelle).</param>
    private static async Task<Ok<List<PayeeDto>>> SearchAsync(AppDbContext db, string? search, int? limit, bool? reimbursers)
    {
        var take = Math.Clamp(limit ?? 20, 1, 100);
        var query = db.Payees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var key = Payee.Normalize(search);
            query = query.Where(p => p.NormalizedName.Contains(key));
        }
        if (reimbursers == true)
        {
            query = query.Where(p => p.Reimburser != Reimburser.None);
        }
        var payees = await query
            .Select(p => new { p.Id, p.Name, p.Reimburser, UsageCount = db.Transactions.Count(t => t.PayeeId == p.Id) })
            .OrderByDescending(p => p.UsageCount).ThenBy(p => p.Name)
            .Take(take)
            .ToListAsync();
        return TypedResults.Ok(payees.ConvertAll(p => new PayeeDto(p.Id, p.Name, p.UsageCount, p.Reimburser)));
    }

    private static async Task<IResult> SetReimburserAsync(Guid id, ReimburserRequest request, AppDbContext db)
    {
        if (!Enum.IsDefined(request.Reimburser))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["reimburser"] = ["Organisme inconnu."] });
        }
        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == id);
        if (payee is null)
        {
            return TypedResults.NotFound();
        }
        payee.Reimburser = request.Reimburser;
        await db.SaveChangesAsync();
        var usage = await db.Transactions.CountAsync(t => t.PayeeId == id);
        return TypedResults.Ok(new PayeeDto(payee.Id, payee.Name, usage, payee.Reimburser));
    }

    /// <summary>Tiers existant de même nom (casse ignorée), ou nouveau tiers ajouté au contexte.</summary>
    public static async Task<Payee?> FindOrCreateAsync(AppDbContext db, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var normalized = Payee.Normalize(name);
        var payee = db.Payees.Local.FirstOrDefault(p => p.NormalizedName == normalized)
            ?? await db.Payees.FirstOrDefaultAsync(p => p.NormalizedName == normalized);
        if (payee is null)
        {
            payee = new Payee { Name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)), NormalizedName = normalized };
            db.Payees.Add(payee);
        }
        return payee;
    }
}
