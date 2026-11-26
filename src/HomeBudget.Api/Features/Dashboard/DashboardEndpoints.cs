using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Forecasts;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Dashboard;

/// <summary>Tableau de bord d'une période : chiffres clés (G1), dépenses par catégorie (G2), budget et réel (G3), 12 périodes (G4).</summary>
public static class DashboardEndpoints
{
    public const int HistoryLength = 12;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        group.MapGet("/{periodKey}", GetAsync)
            .Produces<DashboardDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Tableau de bord de la période (clé AAAA-MM) : chiffres clés, dépenses par catégorie, budget comparé au réel et 12 dernières périodes.");

        return app;
    }

    /// <param name="Income">Revenus réalisés de la période.</param>
    /// <param name="Expenses">Dépenses réalisées (fixes et variables), en positif.</param>
    /// <param name="Savings">Épargne réalisée, en positif.</param>
    /// <param name="Balance">Solde des comptes de la prévision : aujourd'hui pour la période en cours, en fin de période pour une période passée, null pour une période future.</param>
    /// <param name="BalanceDate">Date du solde.</param>
    /// <param name="ForecastBalance">Solde prévu en fin de période (période en cours seulement).</param>
    /// <param name="RemainingToLive">Reste à vivre sur la période (période en cours seulement).</param>
    /// <param name="RemainingPerDay">Reste à vivre par jour restant (période en cours seulement).</param>
    public sealed record KeyFiguresDto(
        decimal Income,
        decimal Expenses,
        decimal Savings,
        decimal? Balance,
        DateOnly? BalanceDate,
        decimal? ForecastBalance,
        decimal? RemainingToLive,
        decimal? RemainingPerDay);

    /// <param name="Amount">Dépense réalisée, sous-catégories comprises, en positif.</param>
    public sealed record CategoryAmountDto(Guid CategoryId, string Name, string? Color, CategoryKind Kind, decimal Amount);

    /// <param name="Amount">Dépenses (fixes et variables) chez ce tiers sur la période, remboursements classés dans ces catégories déduits, en positif.</param>
    public sealed record PayeeAmountDto(Guid PayeeId, string Name, decimal Amount, int Count);

    public sealed record BudgetComparisonDto(
        Guid CategoryId, string Name, string? Color, CategoryKind Kind, decimal Budgeted, decimal Actual, BudgetStatus Status);

    /// <param name="Expenses">Dépenses fixes et variables, en positif.</param>
    /// <param name="Savings">Épargne, en positif.</param>
    public sealed record PeriodTotalsDto(string Key, DateOnly Start, DateOnly End, decimal Income, decimal Expenses, decimal Savings);

    public sealed record DashboardDto(
        PeriodEndpoints.PeriodDto Period,
        KeyFiguresDto KeyFigures,
        List<CategoryAmountDto> ExpensesByCategory,
        List<BudgetComparisonDto> BudgetVsActual,
        List<PeriodTotalsDto> History,
        List<PayeeAmountDto> TopPayees);

    private static async Task<IResult> GetAsync(
        string periodKey, AppDbContext db, TimeProvider clock, IBudgetScheduleSource schedule, ForecastService forecasts, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = PeriodCalculator.From(settings);
        if (!calculator.TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }

        var (categories, _, _, budget, _) = await BudgetEndpoints.ComputeAsync(db, settings, period, schedule, ct);
        var expenses = categories
            .Where(c => c.ParentId is null && c.Kind is CategoryKind.Fixed or CategoryKind.Variable)
            .OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => (Category: c, Result: budget.Categories[c.Id]))
            .ToList();

        return TypedResults.Ok(new DashboardDto(
            PeriodEndpoints.ToDto(calculator, period, clock),
            await KeyFiguresAsync(db, clock, forecasts, period, budget, ct),
            [.. expenses
                .Where(e => e.Result.Actual > 0)
                .OrderByDescending(e => e.Result.Actual)
                .Select(e => new CategoryAmountDto(e.Category.Id, e.Category.Name, e.Category.Color, e.Category.Kind, e.Result.Actual))],
            [.. expenses
                .Where(e => e.Result.Budgeted != 0 || e.Result.Actual != 0)
                .Select(e => new BudgetComparisonDto(
                    e.Category.Id, e.Category.Name, e.Category.Color, e.Category.Kind, e.Result.Budgeted, e.Result.Actual, e.Result.Status))],
            await HistoryAsync(db, calculator, period, ct),
            await TopPayeesAsync(db, period, ct)));
    }

    /// <summary>Nombre de tiers du classement (G7).</summary>
    public const int TopPayeesCount = 10;

    /// <summary>Top des tiers (G7) : dépenses fixes et variables de la période, regroupées par tiers.</summary>
    private static async Task<List<PayeeAmountDto>> TopPayeesAsync(AppDbContext db, Period period, CancellationToken ct)
    {
        var rows = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Id, t.Date, t.PayeeId })
            .Join(db.Categories, x => x.CategoryId, c => (Guid?)c.Id, (x, c) => new { x.Amount, x.Id, x.Date, x.PayeeId, c.Kind })
            .Where(x => x.PayeeId != null && x.Date >= period.Start && x.Date <= period.End
                && (x.Kind == CategoryKind.Fixed || x.Kind == CategoryKind.Variable))
            .Select(x => new { PayeeId = x.PayeeId!.Value, x.Amount, x.Id })
            .ToListAsync(ct);
        var totals = rows.GroupBy(r => r.PayeeId)
            .Select(g => (PayeeId: g.Key, Amount: -g.Sum(r => r.Amount), Count: g.Select(r => r.Id).Distinct().Count()))
            .Where(p => p.Amount > 0)
            .OrderByDescending(p => p.Amount)
            .Take(TopPayeesCount)
            .ToList();
        var ids = totals.Select(t => t.PayeeId).ToList();
        var names = await db.Payees.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return totals.ConvertAll(t => new PayeeAmountDto(t.PayeeId, names.GetValueOrDefault(t.PayeeId, "?"), t.Amount, t.Count));
    }

    private static async Task<KeyFiguresDto> KeyFiguresAsync(
        AppDbContext db, TimeProvider clock, ForecastService forecasts, Period period, BudgetResult budget, CancellationToken ct)
    {
        var actual = budget.Actual;
        var today = Balances.Today(clock);
        if (period.Contains(today))
        {
            var forecast = (await forecasts.ComputeAsync(ct)).Result;
            return new KeyFiguresDto(
                actual.Income, actual.Fixed + actual.Variable, actual.Savings,
                forecast.CurrentBalance, today, forecast.EndBalance, forecast.RemainingToLive.Total, forecast.RemainingToLive.PerDay);
        }
        decimal? balance = period.End < today ? await ForecastAccountsBalanceAsync(db, period.End, ct) : null;
        return new KeyFiguresDto(
            actual.Income, actual.Fixed + actual.Variable, actual.Savings,
            balance, balance is null ? null : period.End, null, null, null);
    }

    /// <summary>Solde, à la fin du jour donné, des comptes inclus dans la prévision.</summary>
    private static async Task<decimal> ForecastAccountsBalanceAsync(AppDbContext db, DateOnly date, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.IncludeInForecast && !a.IsArchived)
            .Select(a => new { a.Id, a.InitialBalance, a.InitialDate })
            .ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var movements = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId) && t.Date <= date)
            .SumAsync(t => t.Amount, ct);
        return movements + accounts.Where(a => a.InitialDate <= date).Sum(a => a.InitialBalance);
    }

    /// <summary>
    /// Revenus, dépenses et épargne des <see cref="HistoryLength"/> périodes qui finissent par <paramref name="last"/>,
    /// en une seule requête groupée.
    /// </summary>
    /// <remarks>
    /// Une période commence le jour N : en reculant chaque date de N − 1 jours, toutes les dates d'une période tombent
    /// dans son mois de début, ce qui permet de grouper par mois en SQL. La clé est ce mois, ou le suivant quand
    /// la période porte le nom du mois où elle se termine (voir <see cref="PeriodCalculator"/>).
    /// </remarks>
    internal static async Task<List<PeriodTotalsDto>> HistoryAsync(AppDbContext db, PeriodCalculator calculator, Period last, CancellationToken ct)
    {
        var periods = new List<Period> { last };
        while (periods.Count < HistoryLength)
        {
            periods.Insert(0, calculator.Previous(periods[0]));
        }
        var shift = last.Start.Day - 1;
        var namedByStart = last.Start.Month == last.Month;
        var first = periods[0].Start;
        var end = last.End;

        // Les lignes sans catégorie (dont les virements entre comptes, R5) ne sont ni revenu, ni dépense, ni épargne.
        var sums = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Date })
            .Join(db.Categories, x => x.CategoryId, c => (Guid?)c.Id, (x, c) => new { x.Amount, x.Date, c.Kind })
            .Where(x => x.Date >= first && x.Date <= end)
            .GroupBy(x => new { x.Date.AddDays(-shift).Year, x.Date.AddDays(-shift).Month, x.Kind })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Kind, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var byKey = sums
            .Select(s =>
            {
                var month = new DateOnly(s.Year, s.Month, 1).AddMonths(namedByStart ? 0 : 1);
                return (Key: $"{month.Year:D4}-{month.Month:D2}", s.Kind, s.Sum);
            })
            .ToLookup(s => s.Key);

        return [.. periods.Select(p =>
        {
            decimal Sum(CategoryKind kind) => byKey[p.Key].Where(s => s.Kind == kind).Sum(s => s.Sum);
            return new PeriodTotalsDto(
                p.Key, p.Start, p.End,
                Sum(CategoryKind.Income),
                -(Sum(CategoryKind.Fixed) + Sum(CategoryKind.Variable)),
                -Sum(CategoryKind.Savings));
        })];
    }
}
