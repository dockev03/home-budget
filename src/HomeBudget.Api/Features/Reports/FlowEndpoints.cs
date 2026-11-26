using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Reports;

/// <summary>
/// Diagramme de flux (G9) : revenus → types (dépenses fixes, variables, épargne) → catégories principales, sur une ou
/// plusieurs périodes. Les virements sans catégorie n'y figurent pas (R5). Un excédent apparaît en « Non dépensé » ;
/// un déficit est comblé par « Pris sur les réserves ».
/// </summary>
public static class FlowEndpoints
{
    public const string Hub = "income";
    public const string Reserves = "reserves";
    public const string Unspent = "unspent";
    public const string UncategorizedIn = "uncategorized-in";
    public const string UncategorizedOut = "uncategorized-out";

    public static IEndpointRouteBuilder MapFlowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reports/flows", GetAsync).WithTags("Reports")
            .Produces<FlowsDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesValidationProblem()
            .WithSummary("Flux de la période « from » (clé AAAA-MM) à la période « to » (incluse, par défaut la même) : nœuds et liens du diagramme de Sankey.");
        return app;
    }

    /// <param name="Id">Identifiant stable : « income », « kind:Fixed », « cat:{id} »…</param>
    /// <param name="Column">Colonne : 0 sources, 1 revenus, 2 types, 3 catégories.</param>
    public sealed record FlowNodeDto(string Id, string Label, string? Color, int Column);

    public sealed record FlowLinkDto(string Source, string Target, decimal Value);

    /// <param name="Income">Total des revenus (catégorisés ou non).</param>
    /// <param name="Outflows">Total des dépenses et de l'épargne.</param>
    public sealed record FlowsDto(string From, string To, DateOnly Start, DateOnly End, decimal Income, decimal Outflows, List<FlowNodeDto> Nodes, List<FlowLinkDto> Links);

    private static async Task<IResult> GetAsync(string from, string? to, AppDbContext db, CancellationToken ct)
    {
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        if (!calculator.TryParse(from, out var first) || !calculator.TryParse(to ?? from, out var last))
        {
            return PeriodEndpoints.UnknownPeriod();
        }
        if (last.Start < first.Start)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["La période de fin précède celle de début."] });
        }
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var (sums, uncategorized) = await BudgetEndpoints.ActualSumsAsync(db, first.Start, last.End, ct);

        // Réalisé par catégorie principale (R2) : positif pour un revenu comme pour une dépense.
        var byMain = new Dictionary<Guid, decimal>();
        foreach (var (id, sum) in sums)
        {
            if (categories.TryGetValue(id, out var c))
            {
                var main = c.ParentId is { } p && categories.TryGetValue(p, out var parent) ? parent : c;
                byMain[main.Id] = byMain.GetValueOrDefault(main.Id) + (main.Kind == CategoryKind.Income ? sum : -sum);
            }
        }

        var nodes = new List<FlowNodeDto> { new(Hub, "Revenus", null, 1) };
        var links = new List<FlowLinkDto>();
        IEnumerable<Category> Mains(CategoryKind kind) => categories.Values
            .Where(c => c.ParentId is null && c.Kind == kind && byMain.GetValueOrDefault(c.Id) > 0)
            .OrderByDescending(c => byMain[c.Id]).ThenBy(c => c.Name, StringComparer.CurrentCulture);

        foreach (var c in Mains(CategoryKind.Income))
        {
            nodes.Add(new FlowNodeDto($"cat:{c.Id}", c.Name, c.Color, 0));
            links.Add(new FlowLinkDto($"cat:{c.Id}", Hub, byMain[c.Id]));
        }
        if (uncategorized > 0)
        {
            nodes.Add(new FlowNodeDto(UncategorizedIn, "Sans catégorie", null, 0));
            links.Add(new FlowLinkDto(UncategorizedIn, Hub, uncategorized));
        }

        var kinds = new (CategoryKind Kind, string Label)[]
        {
            (CategoryKind.Fixed, "Dépenses fixes"), (CategoryKind.Variable, "Dépenses variables"), (CategoryKind.Savings, "Épargne"),
        };
        foreach (var (kind, label) in kinds)
        {
            var mains = Mains(kind).ToList();
            if (mains.Count == 0)
            {
                continue;
            }
            var kindId = $"kind:{kind}";
            nodes.Add(new FlowNodeDto(kindId, label, null, 2));
            links.Add(new FlowLinkDto(Hub, kindId, mains.Sum(c => byMain[c.Id])));
            foreach (var c in mains)
            {
                nodes.Add(new FlowNodeDto($"cat:{c.Id}", c.Name, c.Color, 3));
                links.Add(new FlowLinkDto(kindId, $"cat:{c.Id}", byMain[c.Id]));
            }
        }
        if (uncategorized < 0)
        {
            nodes.Add(new FlowNodeDto(UncategorizedOut, "Sans catégorie", null, 2));
            links.Add(new FlowLinkDto(Hub, UncategorizedOut, -uncategorized));
        }

        var income = links.Where(l => l.Target == Hub).Sum(l => l.Value);
        var outflows = links.Where(l => l.Source == Hub).Sum(l => l.Value);
        if (outflows > income)
        {
            nodes.Add(new FlowNodeDto(Reserves, "Pris sur les réserves", null, 0));
            links.Add(new FlowLinkDto(Reserves, Hub, outflows - income));
        }
        else if (income > outflows)
        {
            nodes.Add(new FlowNodeDto(Unspent, "Non dépensé", null, 2));
            links.Add(new FlowLinkDto(Hub, Unspent, income - outflows));
        }
        if (income == 0 && outflows == 0)
        {
            nodes.Clear();
        }
        return TypedResults.Ok(new FlowsDto(first.Key, last.Key, first.Start, last.End, income, outflows, nodes, links));
    }
}
