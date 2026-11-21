using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Periods;

/// <summary>Périodes budgétaires calculées selon le paramètre du foyer (R1).</summary>
public static class PeriodEndpoints
{
    public static IEndpointRouteBuilder MapPeriodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/periods").WithTags("Periods");

        group.MapGet("/current", CurrentAsync)
            .WithSummary("Période en cours.");
        group.MapGet("/{key}", GetAsync).Produces<PeriodDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Période désignée par sa clé AAAA-MM.");

        return app;
    }

    /// <param name="Key">Clé AAAA-MM : le mois dont la période porte le nom.</param>
    public sealed record PeriodDto(string Key, DateOnly Start, DateOnly End, string PreviousKey, string NextKey, bool IsCurrent);

    public static async Task<PeriodCalculator> CalculatorAsync(AppDbContext db) =>
        PeriodCalculator.From(await db.HouseholdSettings.AsNoTracking().SingleAsync());

    public static PeriodDto ToDto(PeriodCalculator calculator, Period period, TimeProvider clock) =>
        new(period.Key, period.Start, period.End, calculator.Previous(period).Key, calculator.Next(period).Key,
            period.Contains(Balances.Today(clock)));

    public static IResult UnknownPeriod() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Période inconnue.",
        detail: "La période s'écrit AAAA-MM, par exemple 2026-11.");

    private static async Task<Ok<PeriodDto>> CurrentAsync(AppDbContext db, TimeProvider clock)
    {
        var calculator = await CalculatorAsync(db);
        return TypedResults.Ok(ToDto(calculator, calculator.For(Balances.Today(clock)), clock));
    }

    private static async Task<IResult> GetAsync(string key, AppDbContext db, TimeProvider clock)
    {
        var calculator = await CalculatorAsync(db);
        return calculator.TryParse(key, out var period) ? TypedResults.Ok(ToDto(calculator, period, clock)) : UnknownPeriod();
    }
}
