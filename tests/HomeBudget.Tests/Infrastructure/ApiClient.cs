using System.Net.Http.Json;
using System.Text.Json;

namespace HomeBudget.Tests.Infrastructure;

/// <summary>Client HTTP de test qui gère le jeton anti-CSRF comme le front.</summary>
public sealed class ApiClient(HttpClient http)
{
    private string? _csrf;

    public HttpClient Http => http;

    public async Task RefreshCsrfAsync()
    {
        var response = await http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        _csrf = response.GetProperty("token").GetString();
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(new Uri(url, UriKind.Relative));

    public async Task<HttpResponseMessage> PostAsync(string url, object? body = null, bool withCsrf = true)
    {
        if (withCsrf && _csrf is null)
        {
            await RefreshCsrfAsync();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative))
        {
            Content = JsonContent.Create(body ?? new { }),
        };
        if (withCsrf)
        {
            request.Headers.Add("X-CSRF-TOKEN", _csrf);
        }
        return await http.SendAsync(request);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
