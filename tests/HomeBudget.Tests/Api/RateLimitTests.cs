using System.Net;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class RateLimitTests
{
    private sealed class StrictFactory : HomeBudgetFactory
    {
        protected override int AuthRequestsPerMinute => 3;
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        await using var factory = new StrictFactory();
        var client = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsync("/api/auth/login", new { userName = "x", password = "y" })).StatusCode);
        }

        statuses.Take(3).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        statuses[3].ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
