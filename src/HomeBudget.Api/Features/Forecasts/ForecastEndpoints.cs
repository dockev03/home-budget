using HomeBudget.Api.Domain.Forecasts;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Features.Periods;

using Microsoft.AspNetCore.Mvc;

namespace HomeBudget.Api.Features.Forecasts;

/// <summary>Prévision (F1 à F5, F7) et reste à vivre de la période en cours (F6).</summary>
public static class ForecastEndpoints
{
    public static IEndpointRouteBuilder MapForecastEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/forecast").WithTags("Forecast");

        group.MapGet("/", GetAsync)
            .Produces<ForecastDto>().ProducesValidationProblem()
            .WithSummary("Prévision sur 1, 3, 6 ou 12 périodes : solde prévu en fin de chaque période, courbe jour par jour, alerte de seuil, reste à vivre de la période en cours, et série à part avec les scénarios demandés.");

        return app;
    }

    public sealed record ForecastPointDto(DateOnly Date, decimal Balance, bool IsForecast);

    /// <param name="Total">Reste à vivre sur la période (R4).</param>
    /// <param name="PerDay">Reste à vivre par jour restant, aujourd'hui compris.</param>
    public sealed record RemainingToLiveDto(
        decimal Total, decimal PerDay, int DaysLeft, decimal Income, decimal Fixed, decimal Savings, decimal SafetyThreshold);

    /// <param name="EndBalance">Solde prévu en fin de période.</param>
    /// <param name="ScenarioEndBalance">Même solde avec les scénarios (null sans scénario).</param>
    public sealed record ForecastPeriodDto(string Key, DateOnly Start, DateOnly End, decimal EndBalance, decimal? ScenarioEndBalance);

    /// <param name="ScenarioIds">Scénarios pris en compte.</param>
    /// <param name="Daily">Solde avec les scénarios, d'aujourd'hui à la fin de l'horizon.</param>
    /// <param name="EndBalance">Solde prévu en fin d'horizon avec les scénarios.</param>
    /// <param name="Impact">Effet total des scénarios sur l'horizon.</param>
    /// <param name="ThresholdDate">Premier jour sous le seuil avec les scénarios ; null sinon.</param>
    public sealed record ScenarioForecastDto(
        IReadOnlyList<Guid> ScenarioIds, IReadOnlyList<ForecastPointDto> Daily, decimal EndBalance, decimal Impact, DateOnly? ThresholdDate);

    /// <param name="AccountIds">Comptes inclus dans la prévision.</param>
    /// <param name="Strategy">Stratégie de prévision (F7) : par le budget, ou par la moyenne.</param>
    /// <param name="AverageMonths">Nombre de périodes de la moyenne (stratégie Average).</param>
    /// <param name="Horizon">Nombre de périodes prévues, la période en cours comprise (F4).</param>
    /// <param name="CurrentBalance">Solde réel aujourd'hui.</param>
    /// <param name="EndBalance">Solde prévu en fin de période en cours (R3).</param>
    /// <param name="FutureTransactions">Opérations déjà saisies, datées après aujourd'hui, dans la période en cours.</param>
    /// <param name="Scheduled">Échéances de récurrence restantes de la période en cours.</param>
    /// <param name="Budget">Budget (ou moyenne) restant non couvert par une échéance, dans la période en cours.</param>
    /// <param name="Threshold">Seuil d'alerte : somme des seuils des comptes inclus.</param>
    /// <param name="ThresholdDate">Premier jour de l'horizon sous le seuil (aujourd'hui s'il y est déjà) ; null sinon.</param>
    /// <param name="Periods">Solde prévu en fin de chaque période de l'horizon.</param>
    /// <param name="Daily">Solde jour par jour, du début de la période en cours à la fin de l'horizon.</param>
    /// <param name="Scenario">Prévision avec les scénarios demandés (null sans scénario).</param>
    public sealed record ForecastDto(
        PeriodEndpoints.PeriodDto Period,
        IReadOnlyList<Guid> AccountIds,
        bool IncludesIncome,
        ForecastStrategy Strategy,
        int AverageMonths,
        int Horizon,
        decimal CurrentBalance,
        decimal EndBalance,
        decimal FutureTransactions,
        decimal Scheduled,
        decimal Budget,
        decimal Threshold,
        DateOnly? ThresholdDate,
        RemainingToLiveDto RemainingToLive,
        IReadOnlyList<ForecastPeriodDto> Periods,
        IReadOnlyList<ForecastPointDto> Daily,
        ScenarioForecastDto? Scenario);

    /// <param name="horizon">Nombre de périodes : 1 (par défaut), 3, 6 ou 12.</param>
    /// <param name="scenarios">Scénarios à ajouter dans une série à part ; un identifiant inconnu est ignoré.</param>
    private static async Task<IResult> GetAsync(
        ForecastService service, TimeProvider clock, CancellationToken ct, [FromQuery] int? horizon, [FromQuery] Guid[]? scenarios)
    {
        var months = horizon ?? 1;
        if (!ForecastService.Horizons.Contains(months))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["horizon"] = [$"Horizon possible : {string.Join(", ", ForecastService.Horizons)} périodes."],
            });
        }
        var forecast = await service.ComputeAsync(ct, months, scenarios);
        var r = forecast.Result;
        RemainingToLive rav = r.RemainingToLive;
        return TypedResults.Ok(new ForecastDto(
            PeriodEndpoints.ToDto(forecast.Calculator, forecast.Period, clock),
            forecast.AccountIds,
            forecast.IncludesIncome,
            forecast.Strategy,
            forecast.AverageMonths,
            months,
            r.CurrentBalance,
            r.EndBalance,
            r.FutureTransactions,
            r.Scheduled,
            r.Budget,
            r.Threshold,
            r.ThresholdDate,
            new RemainingToLiveDto(rav.Total, rav.PerDay, rav.DaysLeft, rav.Income, rav.Fixed, rav.Savings, rav.SafetyThreshold),
            [.. r.Periods.Select(p => new ForecastPeriodDto(p.Period.Key, p.Period.Start, p.Period.End, p.EndBalance, p.ScenarioEndBalance))],
            [.. r.Daily.Select(ToDto)],
            r.Scenario is { } s
                ? new ScenarioForecastDto(forecast.ScenarioIds, [.. s.Daily.Select(ToDto)], s.EndBalance, s.Impact, s.ThresholdDate)
                : null));
    }

    private static ForecastPointDto ToDto(ForecastPoint p) => new(p.Date, p.Balance, p.IsForecast);
}
