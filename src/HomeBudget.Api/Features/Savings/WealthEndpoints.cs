using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Savings;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Savings;

/// <summary>
/// Évolution du patrimoine net (G5) et épargne de précaution (J3). Seuls les comptes visibles comptent (R8).
/// </summary>
public static class WealthEndpoints
{
    /// <summary>Durées proposées pour l'évolution du patrimoine, en nombre de périodes.</summary>
    public static readonly IReadOnlyList<int> NetWorthLengths = [12, 24, 60];

    public static IEndpointRouteBuilder MapWealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Savings");

        group.MapGet("/net-worth", NetWorthAsync)
            .Produces<NetWorthDto>().ProducesValidationProblem()
            .WithSummary("Patrimoine net en fin de chaque période (aujourd'hui pour la période en cours), sur 12, 24 ou 60 périodes, par type de compte.");
        group.MapGet("/emergency-fund", EmergencyFundAsync)
            .WithSummary("Épargne de précaution : solde des comptes marqués, dépense moyenne par période et nombre de mois couverts.");

        return app;
    }

    public sealed record NetWorthByTypeDto(AccountType Type, decimal Amount);

    /// <param name="Date">Date du solde : fin de la période, ou aujourd'hui pour la période en cours.</param>
    public sealed record NetWorthPointDto(string Key, DateOnly Date, decimal Total, List<NetWorthByTypeDto> ByType);

    public sealed record NetWorthDto(List<NetWorthPointDto> Points);

    public sealed record EmergencyFundAccountDto(Guid Id, string Name, decimal Balance);

    /// <param name="Amount">Solde des comptes d'épargne de précaution visibles et non archivés.</param>
    /// <param name="MonthlyExpenses">Dépenses fixes et variables moyennes par période, en positif.</param>
    /// <param name="AverageMonths">Périodes clôturées retenues pour la moyenne (au plus le paramètre du foyer, et pas avant la première opération).</param>
    /// <param name="MonthsCovered">Mois de dépenses couverts, à une décimale ; null sans dépense moyenne.</param>
    public sealed record EmergencyFundDto(
        decimal Amount, decimal MonthlyExpenses, int AverageMonths, decimal? MonthsCovered, List<EmergencyFundAccountDto> Accounts);

    /// <remarks>
    /// Les comptes archivés comptent aussi : leur historique fait partie du patrimoine passé. Un compte valorisé (A7)
    /// part de la dernière valorisation antérieure à chaque date.
    /// </remarks>
    private static async Task<IResult> NetWorthAsync(AppDbContext db, TimeProvider clock, int? months, CancellationToken ct)
    {
        var length = months ?? NetWorthLengths[0];
        if (!NetWorthLengths.Contains(length))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["months"] = [$"Durée attendue : {string.Join(", ", NetWorthLengths)} périodes."],
            });
        }
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        var today = Balances.Today(clock);
        var periods = new List<Period> { calculator.For(today) };
        while (periods.Count < length)
        {
            periods.Insert(0, calculator.Previous(periods[0]));
        }

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IncludeInNetWorth).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var movements = (await db.Transactions.AsNoTracking()
                .Where(t => ids.Contains(t.AccountId) && t.Date <= today)
                .GroupBy(t => new { t.AccountId, t.Date })
                .Select(g => new { g.Key.AccountId, g.Key.Date, Sum = g.Sum(t => t.Amount) })
                .ToListAsync(ct))
            .ToLookup(m => m.AccountId, m => new DatedAmount(m.Date, m.Sum));
        var valuations = (await db.AccountValuations.AsNoTracking()
                .Where(v => ids.Contains(v.AccountId))
                .Select(v => new { v.AccountId, v.Date, v.Value })
                .ToListAsync(ct))
            .ToLookup(v => v.AccountId, v => new DatedAmount(v.Date, v.Value));
        var histories = accounts.Select(a => (a.Type, History: new BalanceHistory(
            a.InitialBalance, a.InitialDate, [.. valuations[a.Id]], [.. movements[a.Id]]))).ToList();

        return TypedResults.Ok(new NetWorthDto([.. periods.Select(p =>
        {
            var date = p.End < today ? p.End : today;
            var byType = histories
                .GroupBy(h => h.Type)
                .Select(g => new NetWorthByTypeDto(g.Key, g.Sum(h => h.History.At(date))))
                .Where(t => t.Amount != 0)
                .OrderBy(t => t.Type)
                .ToList();
            return new NetWorthPointDto(p.Key, date, byType.Sum(t => t.Amount), byType);
        })]));
    }

    private static async Task<Ok<EmergencyFundDto>> EmergencyFundAsync(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = PeriodCalculator.From(settings);
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.IsEmergencyFund && !a.IsArchived)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);
        var balances = await Balances.ComputeAsync(db, clock, accounts, ct);
        var amount = accounts.Sum(a => balances[a.Id].Current);

        // Moyenne des périodes clôturées, sans remonter avant la période de la première opération.
        var first = await db.Transactions.AsNoTracking().OrderBy(t => t.Date).Select(t => (DateOnly?)t.Date).FirstOrDefaultAsync(ct);
        var last = calculator.Previous(calculator.For(Balances.Today(clock)));
        var count = 0;
        var from = last;
        decimal expenses = 0;
        if (first is { } firstDate && last.End >= firstDate)
        {
            var earliest = calculator.For(firstDate);
            count = 1;
            while (count < settings.EmergencyFundAverageMonths && from.Start > earliest.Start)
            {
                from = calculator.Previous(from);
                count++;
            }
            var (sums, _) = await BudgetEndpoints.ActualSumsAsync(db, from.Start, last.End, ct);
            var kinds = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Kind, ct);
            var total = -sums.Where(s => kinds.GetValueOrDefault(s.Key) is CategoryKind.Fixed or CategoryKind.Variable).Sum(s => s.Value);
            expenses = Math.Max(0m, Math.Round(total / count, 2, MidpointRounding.AwayFromZero));
        }

        return TypedResults.Ok(new EmergencyFundDto(
            amount, expenses, count, EmergencyFund.MonthsCovered(amount, expenses),
            [.. accounts.Select(a => new EmergencyFundAccountDto(a.Id, a.Name, balances[a.Id].Current))]));
    }
}
