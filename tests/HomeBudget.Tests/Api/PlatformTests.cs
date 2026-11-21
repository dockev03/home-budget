using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class PlatformTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_anonymous(string url)
    {
        await using var factory = new HomeBudgetFactory();

        var response = await factory.CreateApiClient().GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        await using var factory = new HomeBudgetFactory();

        var response = await factory.CreateApiClient().GetAsync("/api/setup/status");

        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'self';");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_404()
    {
        await using var factory = new HomeBudgetFactory();

        var response = await factory.CreateApiClient().GetAsync("/api/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Metrics_are_hidden_when_requested_through_proxy()
    {
        await using var factory = new HomeBudgetFactory();
        var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Add("X-Forwarded-For", "203.0.113.10");

        var response = await client.Http.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/docs/")]
    public async Task Api_documentation_requires_admin(string url)
    {
        await using var factory = new HomeBudgetFactory();
        (await factory.CreateApiClient().GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var admin = await factory.CreateAdminClientAsync();
        (await admin.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
