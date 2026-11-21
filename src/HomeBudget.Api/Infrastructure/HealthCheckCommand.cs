namespace HomeBudget.Api.Infrastructure;

/// <summary>
/// « dotnet HomeBudget.Api.dll --healthcheck » : utilisé par le HEALTHCHECK Docker
/// (l'image runtime ne contient ni curl ni wget).
/// </summary>
public static class HealthCheckCommand
{
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split([',', ';'])[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
