using HomeBudget.Api.Features.Account;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Advances;
using HomeBudget.Api.Features.Attachments;
using HomeBudget.Api.Features.Audit;
using HomeBudget.Api.Features.Auth;
using HomeBudget.Api.Features.Backups;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Calendar;
using HomeBudget.Api.Features.Categories;
using HomeBudget.Api.Features.Dashboard;
using HomeBudget.Api.Features.Exports;
using HomeBudget.Api.Features.Family;
using HomeBudget.Api.Features.Forecasts;
using HomeBudget.Api.Features.Imports;
using HomeBudget.Api.Features.Integrations;
using HomeBudget.Api.Features.Notifications;
using HomeBudget.Api.Features.Payees;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Features.Reconciliations;
using HomeBudget.Api.Features.Recurrences;
using HomeBudget.Api.Features.Reports;
using HomeBudget.Api.Features.Rules;
using HomeBudget.Api.Features.Savings;
using HomeBudget.Api.Features.Scenarios;
using HomeBudget.Api.Features.Settings;
using HomeBudget.Api.Features.Setup;
using HomeBudget.Api.Features.Split;
using HomeBudget.Api.Features.Sync;
using HomeBudget.Api.Features.Tags;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Features.Users;
using HomeBudget.Api.Infrastructure.Http;
using HomeBudget.Api.Infrastructure.Security;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Net.Http.Headers;

using Scalar.AspNetCore;

namespace HomeBudget.Api.Infrastructure;

public static class HomeBudgetPipeline
{
    public static WebApplication UseHomeBudget(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<MetricsAccessMiddleware>();

        // PWA : fichiers statiques servis sans authentification (l'API reste protégée).
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers[HeaderNames.CacheControl] = ctx.Context.Request.Path.StartsWithSegments("/assets")
                    ? "public, max-age=31536000, immutable" // fichiers Vite avec empreinte dans le nom
                    : "no-cache";
            },
        });

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<ApiTokenRestrictionMiddleware>();
        app.UseMiddleware<TwoFactorEnforcementMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<BodySizeLimitMiddleware>();
        app.UseMiddleware<AntiforgeryValidationMiddleware>();
        return app;
    }

    public static WebApplication MapHomeBudgetEndpoints(this WebApplication app)
    {
        app.MapSetupEndpoints();
        app.MapAuthEndpoints();
        app.MapTwoFactorEndpoints();
        app.MapAccountEndpoints();
        app.MapValuationEndpoints();
        app.MapLoanEndpoints();
        app.MapCategoryEndpoints();
        app.MapPayeeEndpoints();
        app.MapTransactionEndpoints();
        app.MapTransactionHistoryEndpoints();
        app.MapTransferEndpoints();
        app.MapSettingsEndpoints();
        app.MapPeriodEndpoints();
        app.MapBudgetEndpoints();
        app.MapProvisionEndpoints();
        app.MapAnnualReportEndpoints();
        app.MapCustomReportEndpoints();
        app.MapSavedReportEndpoints();
        app.MapFlowEndpoints();
        app.MapRecurrenceEndpoints();
        app.MapForecastEndpoints();
        app.MapScenarioEndpoints();
        app.MapSavingsGoalEndpoints();
        app.MapWealthEndpoints();
        app.MapDashboardLayoutEndpoints();
        app.MapDashboardEndpoints();
        app.MapExportEndpoints();
        app.MapBackupEndpoints();
        app.MapImportProfileEndpoints();
        app.MapImportEndpoints();
        app.MapTagEndpoints();
        app.MapRuleEndpoints();
        app.MapReconciliationEndpoints();
        app.MapAdvanceEndpoints();
        app.MapSubscriptionEndpoints();
        app.MapCalendarEndpoints();
        app.MapCategoryMergeEndpoints();
        app.MapUserEndpoints();
        app.MapInvitationEndpoints();
        app.MapPasswordEndpoints();
        app.MapAvatarEndpoints();
        app.MapSessionEndpoints();
        app.MapAuditEndpoints();
        app.MapSplitEndpoints();
        app.MapFamilyEndpoints();
        app.MapNotificationEndpoints();
        app.MapSyncEndpoints();
        app.MapAttachmentEndpoints();
        app.MapApiTokenEndpoints();
        app.MapWebhookEndpoints();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous(); // restreint par MetricsAccessMiddleware

        // Documentation de l'API publique (I5) : ouverte à tout utilisateur connecté, la description aussi aux jetons.
        app.MapOpenApi().RequireAuthorization();
        app.MapScalarApiReference("/api/docs", o => o
                .WithTitle("Home Budget API")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                // Aucune ressource ni service externe : polices système, ni agent IA, ni télémétrie.
                .DisableDefaultFonts()
                .DisableAgent()
                .DisableTelemetry()
                .HideDeveloperTools())
            .RequireAuthorization();

        app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ressource introuvable."))
            .AllowAnonymous().ExcludeFromDescription();
        app.MapFallbackToFile("index.html").AllowAnonymous();
        return app;
    }
}
