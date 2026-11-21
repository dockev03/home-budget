using HomeBudget.Api.Features.Setup;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.Extensions.Options;

if (args.Contains("--healthcheck"))
{
    return await HealthCheckCommand.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddHomeBudget();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<HomeBudgetOptions>>().Value;
if (options.Security.AllowInsecureHttp && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning("HB__Security__AllowInsecureHttp est activé hors développement : les cookies de session peuvent circuler en clair.");
}

await DatabaseInitializer.InitializeAsync(app.Services);
await SetupEndpoints.ActivateIfNeededAsync(app.Services, options.SetupToken, app.Logger);

app.UseHomeBudget();
app.MapHomeBudgetEndpoints();

await app.RunAsync();
return 0;
