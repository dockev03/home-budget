using HomeBudget.Api.Features.Setup;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.Extensions.Options;

if (args.Contains("--healthcheck"))
{
    return await HealthCheckCommand.RunAsync();
}

if (args.Contains("--generate-vapid-keys"))
{
    // Clés des notifications push, à placer dans HB__Push__VapidPublicKey et HB__Push__VapidPrivateKey.
    var (publicKey, privateKey) = HomeBudget.Api.Infrastructure.Notifications.Vapid.Generate();
    Console.WriteLine($"HB__Push__VapidPublicKey={publicKey}");
    Console.WriteLine($"HB__Push__VapidPrivateKey={privateKey}");
    return 0;
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
