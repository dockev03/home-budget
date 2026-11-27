using HomeBudget.Api.Infrastructure.Security;

namespace HomeBudget.Tests.Domain;

public sealed class UserAgentInfoTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Chrome 131", "Windows", DeviceKind.Desktop)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.2903.86",
        "Edge 131", "Windows", DeviceKind.Desktop)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0",
        "Firefox 133", "Windows", DeviceKind.Desktop)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0",
        "Firefox 133", "Linux", DeviceKind.Desktop)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15",
        "Safari 18", "macOS", DeviceKind.Desktop)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
        "Safari 17", "iOS 17", DeviceKind.Mobile)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/131.0.6778.73 Mobile/15E148 Safari/604.1",
        "Chrome 131", "iOS 18", DeviceKind.Mobile)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
        "Safari 17", "iPadOS 17", DeviceKind.Tablet)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/26.0 Chrome/122.0.0.0 Mobile Safari/537.36",
        "Samsung Internet 26", "Android 14", DeviceKind.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
        "Chrome 131", "Android 14", DeviceKind.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X200) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Chrome 131", "Android 13", DeviceKind.Tablet)]
    [InlineData("Mozilla/5.0 (Android 14; Mobile; rv:133.0) Gecko/133.0 Firefox/133.0",
        "Firefox 133", "Android 14", DeviceKind.Mobile)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 OPR/115.0.0.0",
        "Opera 115", "Windows", DeviceKind.Desktop)]
    [InlineData("curl/8.5.0", "curl 8", "Inconnu", DeviceKind.Unknown)]
    public void Recognizes_common_browsers(string userAgent, string browser, string os, DeviceKind device)
    {
        var info = UserAgentInfo.Parse(userAgent);

        info.Browser.ShouldBe(browser);
        info.OperatingSystem.ShouldBe(os);
        info.Device.ShouldBe(device);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_user_agent_is_unknown(string? userAgent)
    {
        var info = UserAgentInfo.Parse(userAgent);

        info.ShouldBe(new UserAgentInfo("Inconnu", "Inconnu", DeviceKind.Unknown));
        info.Describe().ShouldBe("Inconnu · Inconnu");
    }

    [Fact]
    public void Describe_includes_the_device_kind()
    {
        UserAgentInfo.Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0").Describe()
            .ShouldBe("Firefox 133 · Windows (ordinateur)");
    }
}
