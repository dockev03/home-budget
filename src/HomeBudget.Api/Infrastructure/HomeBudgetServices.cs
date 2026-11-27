using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Account;
using HomeBudget.Api.Features.Attachments;
using HomeBudget.Api.Features.Backups;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Forecasts;
using HomeBudget.Api.Features.Imports;
using HomeBudget.Api.Features.Notifications;
using HomeBudget.Api.Features.Recurrences;
using HomeBudget.Api.Infrastructure.Backups;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Notifications;
using HomeBudget.Api.Infrastructure.Security;
using HomeBudget.Api.Infrastructure.Webhooks;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace HomeBudget.Api.Infrastructure;

public static class HomeBudgetServices
{
    public const string CookieName = "hb_session";

    public static WebApplicationBuilder AddHomeBudget(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var section = builder.Configuration.GetSection(HomeBudgetOptions.Section);
        var options = section.Get<HomeBudgetOptions>() ?? new HomeBudgetOptions();

        // Les chemins relatifs sont résolus depuis le dossier de l'application.
        options.DataPath = Path.GetFullPath(options.DataPath, builder.Environment.ContentRootPath);
        services.AddOptions<HomeBudgetOptions>().Bind(section)
            .PostConfigure(o => o.DataPath = options.DataPath);
        Directory.CreateDirectory(options.DataPath);

        builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditInterceptor>();
        services.AddSingleton<TransactionHistoryInterceptor>();
        services.AddSingleton<WebhookEventInterceptor>();
        services.AddDbContext<AppDbContext>((sp, db) => db
            .UseSqlite($"Data Source={options.DatabasePath}")
            .AddInterceptors(
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<TransactionHistoryInterceptor>(),
                sp.GetRequiredService<WebhookEventInterceptor>()));

        // Clés de chiffrement des cookies persistées : les sessions survivent aux redémarrages.
        services.AddDataProtection()
            .SetApplicationName("HomeBudget")
            .PersistKeysToFileSystem(Directory.CreateDirectory(options.KeysPath));

        var secure = options.Security.AllowInsecureHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        AddIdentity(services, secure);
        AddAuthorization(services);
        AddRateLimiting(services, options);

        services.AddAntiforgery(o =>
        {
            o.HeaderName = AntiforgeryValidationMiddleware.HeaderName;
            o.Cookie.Name = "hb_csrf";
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.HttpOnly = true;
        });

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in IpNetworkList.Parse(options.KnownProxyNetwork).Networks)
            {
                o.KnownIPNetworks.Add(network);
            }
        });

        services.AddProblemDetails();
        services.AddValidation();
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Nombres stricts : un montant envoyé en texte est refusé, et l'OpenAPI type les nombres sans « | string ».
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddOpenApi(o => o.AddDocumentTransformer<OpenApiSecurityTransformer>());

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("home-budget"))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddMeter("System.Runtime")
                .AddPrometheusExporter());

        services.AddSingleton<SetupTokenStore>();
        services.AddSingleton<BackupService>();
        services.AddScoped<IBudgetScheduleSource, RecurrenceBudgetSource>();
        services.AddScoped<RecurrenceGenerator>();
        services.AddScoped<ForecastService>();
        services.AddScoped<BackupRestorer>();
        services.AddScoped<ImportService>();
        services.AddScoped<AuditTrail>();
        services.AddScoped<UserSessions>();
        services.AddHostedService<BackupWorker>();
        services.AddHostedService<RecurrenceWorker>();

        // Notifications (M1, M2) : Web Push signé VAPID et e-mail SMTP, inactifs sans configuration.
        services.AddHttpClient(WebPushSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<WebPushSender>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<AlertService>();
        services.AddHostedService<AlertWorker>();
        services.AddSingleton<SecurityAlertQueue>();
        services.AddHostedService<SecurityAlertWorker>();
        services.AddHostedService<LoginHistoryCleanupWorker>();

        // Pièces jointes (B7).
        services.AddSingleton<AttachmentStore>();
        services.AddHostedService<AttachmentCleanupWorker>();

        // Webhooks (I5) : connexion limitée aux adresses autorisées, sans proxy ni redirection.
        services.AddHttpClient(WebhookSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                ConnectCallback = (context, ct) => WebhookAddressPolicy.ConnectAsync(context, options.Webhooks.AllowPrivateNetworks, ct),
            });
        services.AddSingleton<WebhookSecrets>();
        services.AddSingleton<WebhookSender>();
        services.AddScoped<WebhookDispatcher>();
        services.AddHostedService<WebhookWorker>();
        return builder;
    }

    private static void AddIdentity(IServiceCollection services, CookieSecurePolicy secure)
    {
        // Session par cookie, ou jeton d'accès personnel (I5) quand la requête porte l'en-tête Authorization: Bearer.
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = ApiTokens.SelectorScheme;
                o.DefaultChallengeScheme = ApiTokens.SelectorScheme;
                o.DefaultForbidScheme = ApiTokens.SelectorScheme;
            })
            .AddPolicyScheme(ApiTokens.SelectorScheme, null, o => o.ForwardDefaultSelector = ctx =>
                ApiTokens.HasBearer(ctx.Request) ? ApiTokens.Scheme : IdentityConstants.ApplicationScheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokens.Scheme, null);
        services.AddAuthentication().AddIdentityCookies();

        services.AddIdentityCore<AppUser>(o =>
            {
                // Recommandations NIST/ANSSI : longueur plutôt que règles de composition.
                o.Password.RequiredLength = 12;
                o.Password.RequiredUniqueChars = 4;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@";
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<FrenchIdentityErrorDescriber>();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx => WriteStatus(ctx, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => WriteStatus(ctx, StatusCodes.Status403Forbidden);
            // Session revalidée à chaque requête : désactivation et changement de droits pris en compte aussitôt.
            o.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
            // Sessions suivies côté serveur (N8) : chaque cookie désigne une ligne UserSession.
            o.Events.OnSigningIn = UserSessions.OnSigningInAsync;
        });

        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.Name = "hb_2fa";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });
    }

    private static void AddAuthorization(IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            // Sécurisé par défaut : tout endpoint exige une session, sauf AllowAnonymous explicite.
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AuthorizationPolicies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(AuthorizationPolicies.Write, p => p.RequireRole(Roles.Admin, Roles.Member));

    private static void AddRateLimiting(IServiceCollection services, HomeBudgetOptions options) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                return new ValueTask(Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Trop de tentatives.",
                    detail: "Patientez une minute avant de réessayer.").ExecuteAsync(ctx.HttpContext));
            };
            // Jetons d'accès personnels (I5) : débit limité par jeton (empreinte de l'en-tête), avant même l'authentification.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => ApiTokens.HasBearer(http.Request)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    ApiTokens.Hash(http.Request.Headers.Authorization.ToString()),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.Security.ApiRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter(string.Empty));
            o.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.Security.AuthRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

    private static Task WriteStatus(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        ctx.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
