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

    /// <summary>POST d'un contenu brut (fichier envoyé tel quel), avec le jeton anti-CSRF.</summary>
    public async Task<HttpResponseMessage> PostContentAsync(string url, HttpContent content)
    {
        if (_csrf is null)
        {
            await RefreshCsrfAsync();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative)) { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", _csrf);
        return await http.SendAsync(request);
    }

    public Task<HttpResponseMessage> PutAsync(string url, object body, int? version) =>
        SendAsync(HttpMethod.Put, url, body, version);

    public Task<HttpResponseMessage> DeleteAsync(string url, int? version) =>
        SendAsync(HttpMethod.Delete, url, null, version);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, int? version)
    {
        if (_csrf is null)
        {
            await RefreshCsrfAsync();
        }
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Add("X-CSRF-TOKEN", _csrf);
        if (version is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }
        return await http.SendAsync(request);
    }

    /// <summary>POST attendu en succès ; renvoie le JSON de la réponse.</summary>
    public async Task<JsonElement> CreateAsync(string url, object body)
    {
        var response = await PostAsync(url, body);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
        return await ReadJsonAsync(response);
    }

    public async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
