using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Reports;

/// <summary>Récapitulatif annuel par catégorie, comparé à l'année précédente (P3).</summary>
public static class AnnualReportEndpoints
{
    public static IEndpointRouteBuilder MapAnnualReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports");

        group.MapGet("/annual/{year:int}", GetAsync)
            .Produces<AnnualReportDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Réalisé de l'année par catégorie et par type, comparé à l'année précédente. L'année réunit les 12 périodes qui portent son nom (R1).");

        return app;
    }

    /// <param name="Remaining">Revenus − Fixes − Variables − Épargne.</param>
    public sealed record AnnualTotalsDto(decimal Income, decimal Fixed, decimal Variable, decimal Savings, decimal Remaining);

    /// <param name="Actual">Réalisé de l'année, sous-catégories comprises : positif pour une dépense comme pour un revenu.</param>
    /// <param name="PreviousActual">Réalisé de l'année précédente, calculé de la même façon.</param>
    /// <param name="ChangePercent">Évolution en % (null si l'année précédente est à 0).</param>
    public sealed record AnnualLineDto(
        Guid CategoryId,
        Guid? ParentId,
        string Name,
        CategoryKind Kind,
        string? Color,
        string? Icon,
        decimal Actual,
        decimal PreviousActual,
        decimal Difference,
        decimal? ChangePercent);

    /// <param name="Uncategorized">Somme signée des opérations sans catégorie (hors virements) de l'année.</param>
    public sealed record AnnualReportDto(
        int Year,
        DateOnly Start,
        DateOnly End,
        AnnualTotalsDto Actual,
        AnnualTotalsDto Previous,
        decimal Uncategorized,
        decimal PreviousUncategorized,
        List<AnnualLineDto> Categories);

    private static async Task<IResult> GetAsync(int year, AppDbContext db, CancellationToken ct)
    {
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        if (year is < 1901 or > 9998
            || !calculator.TryParse($"{year:D4}-01", out var first) || !calculator.TryParse($"{year:D4}-12", out var last)
            || !calculator.TryParse($"{year - 1:D4}-01", out var previousFirst) || !calculator.TryParse($"{year - 1:D4}-12", out var previousLast))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Année inconnue.");
        }

        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var (current, uncategorized) = await BudgetEndpoints.ActualSumsAsync(db, first.Start, last.End, ct);
        var (previous, previousUncategorized) = await BudgetEndpoints.ActualSumsAsync(db, previousFirst.Start, previousLast.End, ct);

        decimal Own(Dictionary<Guid, decimal> sums, Category c) => (c.Kind == CategoryKind.Income ? 1m : -1m) * sums.GetValueOrDefault(c.Id);
        decimal Total(Dictionary<Guid, decimal> sums, Category c) =>
            Own(sums, c) + categories.Where(x => x.ParentId == c.Id).Sum(x => Own(sums, x));

        AnnualLineDto Line(Category c)
        {
            var actual = Total(current, c);
            var before = Total(previous, c);
            return new AnnualLineDto(
                c.Id, c.ParentId, c.Name, c.Kind, c.Color, c.Icon, actual, before, actual - before,
                before != 0 ? Math.Round((actual - before) / Math.Abs(before) * 100m, 1, MidpointRounding.AwayFromZero) : null);
        }

        var lines = new List<AnnualLineDto>();
        foreach (var parent in categories.Where(c => c.ParentId is null).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var line = Line(parent);
            if (line is { Actual: 0, PreviousActual: 0 })
            {
                continue;
            }
            lines.Add(line);
            lines.AddRange(categories.Where(c => c.ParentId == parent.Id).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(Line).Where(l => l is not { Actual: 0, PreviousActual: 0 }));
        }

        return TypedResults.Ok(new AnnualReportDto(
            year, first.Start, last.End, Totals(categories, c => Own(current, c)), Totals(categories, c => Own(previous, c)),
            uncategorized, previousUncategorized, lines));
    }

    private static AnnualTotalsDto Totals(List<Category> categories, Func<Category, decimal> amount)
    {
        decimal Sum(CategoryKind kind) => categories.Where(c => c.Kind == kind).Sum(amount);
        var (income, fixedCosts, variable, savings) = (Sum(CategoryKind.Income), Sum(CategoryKind.Fixed), Sum(CategoryKind.Variable), Sum(CategoryKind.Savings));
        return new AnnualTotalsDto(income, fixedCosts, variable, savings, income - fixedCosts - variable - savings);
    }
}
