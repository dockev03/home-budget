using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Settings;

/// <summary>Paramètres du foyer : période budgétaire (R1, D10), seuils des jauges (D9) et stratégie de prévision (F7).</summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/", GetAsync)
            .WithSummary("Paramètres du foyer.");
        group.MapPut("/", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .Produces<SettingsDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie les paramètres du foyer (administrateurs, If-Match requis).");

        return app;
    }

    public sealed record SettingsRequest(
        PeriodMode PeriodMode,
        [property: Range(1, PeriodCalculator.MaxStartDay)] int PeriodStartDay,
        [property: Range(1, 100)] int BudgetWarningPercent,
        [property: Range(1, 200)] int BudgetAlertPercent,
        ForecastStrategy ForecastStrategy = ForecastStrategy.Budget,
        [property: Range(1, HouseholdSettings.MaxForecastAverageMonths)] int ForecastAverageMonths = HouseholdSettings.DefaultForecastAverageMonths);

    /// <param name="ForecastStrategy">Prévision par le budget, ou par la moyenne des dernières périodes (F7).</param>
    /// <param name="ForecastAverageMonths">Nombre de périodes clôturées de la moyenne.</param>
    public sealed record SettingsDto(
        PeriodMode PeriodMode, int PeriodStartDay, int BudgetWarningPercent, int BudgetAlertPercent,
        ForecastStrategy ForecastStrategy, int ForecastAverageMonths, int Version);

    private static async Task<Ok<SettingsDto>> GetAsync(AppDbContext db) =>
        TypedResults.Ok(ToDto(await db.HouseholdSettings.AsNoTracking().SingleAsync()));

    private static async Task<IResult> UpdateAsync(SettingsRequest request, HttpRequest http, AppDbContext db)
    {
        var settings = await db.HouseholdSettings.SingleAsync();
        if (Concurrency.Check(http, settings) is { } precondition)
        {
            return precondition;
        }
        if (!Enum.IsDefined(request.ForecastStrategy))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["forecastStrategy"] = ["Stratégie de prévision inconnue."],
            });
        }
        if (request.BudgetWarningPercent >= request.BudgetAlertPercent)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["budgetWarningPercent"] = ["Le seuil d'avertissement doit être inférieur au seuil d'alerte."],
            });
        }

        settings.PeriodMode = request.PeriodMode;
        settings.PeriodStartDay = request.PeriodStartDay;
        settings.BudgetWarningPercent = request.BudgetWarningPercent;
        settings.BudgetAlertPercent = request.BudgetAlertPercent;
        settings.ForecastStrategy = request.ForecastStrategy;
        settings.ForecastAverageMonths = request.ForecastAverageMonths;
        return await db.TrySaveAsync() ? TypedResults.Ok(ToDto(settings)) : Concurrency.Conflict();
    }

    private static SettingsDto ToDto(HouseholdSettings s) =>
        new(s.PeriodMode, s.PeriodStartDay, s.BudgetWarningPercent, s.BudgetAlertPercent, s.ForecastStrategy, s.ForecastAverageMonths, s.Version);
}
