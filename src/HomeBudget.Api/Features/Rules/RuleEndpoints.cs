using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Rules;

/// <summary>Règles apprises (I2, I3) : consultation, suppression d'une règle erronée, et suggestion à la saisie.</summary>
public static class RuleEndpoints
{
    public static IEndpointRouteBuilder MapRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rules").WithTags("Rules");
        group.MapGet("/", ListAsync)
            .WithSummary("Règles apprises des corrections : clé du libellé bancaire → tiers et catégorie.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une règle apprise (If-Match requis). Les opérations ne sont pas modifiées.");

        app.MapGet("/api/suggestions", SuggestAsync).WithTags("Rules")
            .WithSummary("Tiers et catégorie proposés d'après un libellé (règles apprises) et l'historique du tiers.");
        return app;
    }

    public sealed record RuleDto(Guid Id, string Key, Guid? PayeeId, string? PayeeName, Guid? CategoryId, DateTimeOffset UpdatedAt, int Version);

    /// <param name="PayeeName">Tiers proposé (d'après le libellé), ou null.</param>
    /// <param name="CategoryId">Catégorie proposée, ou null.</param>
    public sealed record SuggestionDto(string? PayeeName, Guid? CategoryId);

    private static async Task<Ok<List<RuleDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var rules = await db.LabelRules.AsNoTracking().OrderBy(r => r.Key).ToListAsync(ct);
        var payeeIds = rules.Select(r => r.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return TypedResults.Ok(rules.ConvertAll(r => new RuleDto(
            r.Id, r.Key, r.PayeeId, r.PayeeId is { } p ? payees.GetValueOrDefault(p) : null, r.CategoryId, r.UpdatedAt, r.Version)));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db)
    {
        var rule = await db.LabelRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, rule) is { } precondition)
        {
            return precondition;
        }
        db.LabelRules.Remove(rule);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    /// <summary>
    /// Une règle apprise pour le libellé donne le tiers et la catégorie. Si le tiers est choisi par l'utilisateur,
    /// sa catégorie habituelle prime, sauf si la règle désigne ce même tiers.
    /// </summary>
    private static async Task<Ok<SuggestionDto>> SuggestAsync(AppDbContext db, string? label, string? payeeName, CancellationToken ct)
    {
        LabelSuggestion? rule = null;
        if (!string.IsNullOrWhiteSpace(label))
        {
            (await LabelRules.SuggestByLabelAsync(db, [label], ct)).TryGetValue(LabelKey.From(label), out rule);
        }

        if (string.IsNullOrWhiteSpace(payeeName))
        {
            return TypedResults.Ok(new SuggestionDto(rule?.PayeeName, rule?.CategoryId));
        }
        var normalized = Payee.Normalize(payeeName);
        var payeeId = await db.Payees.AsNoTracking().Where(p => p.NormalizedName == normalized).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        Guid? categoryId = null;
        if (rule is { CategoryId: not null } && rule.PayeeId == payeeId)
        {
            categoryId = rule.CategoryId;
        }
        else if (payeeId is { } id)
        {
            categoryId = (await LabelRules.CategoriesByPayeeAsync(db, [id], ct)).GetValueOrDefault(id);
        }
        return TypedResults.Ok(new SuggestionDto(rule?.PayeeName, categoryId));
    }
}
