using HomeBudget.Api.Domain.Forecasts;
using HomeBudget.Api.Features.Periods;

using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBudget.Api.Features.Forecasts;

/// <summary>Prévision de la période en cours (F1, F2, F3) et reste à vivre (F6).</summary>
public static class ForecastEndpoints
{
    public static IEndpointRouteBuilder MapForecastEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/forecast").WithTags("Forecast");

        group.MapGet("/", GetAsync)
            .WithSummary("Solde prévu en fin de période, courbe jour par jour, alerte de seuil et reste à vivre de la période en cours.");

        return app;
    }

    public sealed record ForecastPointDto(DateOnly Date, decimal Balance, bool IsForecast);

    /// <param name="Total">Reste à vivre sur la période (R4).</param>
    /// <param name="PerDay">Reste à vivre par jour restant, aujourd'hui compris.</param>
    public sealed record RemainingToLiveDto(
        decimal Total, decimal PerDay, int DaysLeft, decimal Income, decimal Fixed, decimal Savings, decimal SafetyThreshold);

    /// <param name="AccountIds">Comptes inclus dans la prévision.</param>
    /// <param name="CurrentBalance">Solde réel aujourd'hui.</param>
    /// <param name="EndBalance">Solde prévu en fin de période (R3).</param>
    /// <param name="FutureTransactions">Opérations déjà saisies, datées après aujourd'hui.</param>
    /// <param name="Scheduled">Échéances de récurrence restantes.</param>
    /// <param name="Budget">Budget restant non couvert par une échéance.</param>
    /// <param name="Threshold">Seuil d'alerte : somme des seuils des comptes inclus.</param>
    /// <param name="ThresholdDate">Premier jour sous le seuil (aujourd'hui s'il y est déjà) ; null sinon.</param>
    public sealed record ForecastDto(
        PeriodEndpoints.PeriodDto Period,
        IReadOnlyList<Guid> AccountIds,
        bool IncludesIncome,
        decimal CurrentBalance,
        decimal EndBalance,
        decimal FutureTransactions,
        decimal Scheduled,
        decimal Budget,
        decimal Threshold,
        DateOnly? ThresholdDate,
        RemainingToLiveDto RemainingToLive,
        IReadOnlyList<ForecastPointDto> Daily);

    private static async Task<Ok<ForecastDto>> GetAsync(ForecastService service, TimeProvider clock, CancellationToken ct)
    {
        var forecast = await service.ComputeAsync(ct);
        var r = forecast.Result;
        RemainingToLive rav = r.RemainingToLive;
        return TypedResults.Ok(new ForecastDto(
            PeriodEndpoints.ToDto(forecast.Calculator, forecast.Period, clock),
            forecast.AccountIds,
            forecast.IncludesIncome,
            r.CurrentBalance,
            r.EndBalance,
            r.FutureTransactions,
            r.Scheduled,
            r.Budget,
            r.Threshold,
            r.ThresholdDate,
            new RemainingToLiveDto(rav.Total, rav.PerDay, rav.DaysLeft, rav.Income, rav.Fixed, rav.Savings, rav.SafetyThreshold),
            [.. r.Daily.Select(p => new ForecastPointDto(p.Date, p.Balance, p.IsForecast))]));
    }
}
