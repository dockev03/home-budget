using System.Text.RegularExpressions;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>Type d'appareil déduit de l'en-tête User-Agent.</summary>
public enum DeviceKind
{
    Unknown,
    Desktop,
    Mobile,
    Tablet,
}

/// <summary>
/// Navigateur, système et type d'appareil lus dans l'en-tête User-Agent, pour reconnaître une session (N8, N9).
/// Lecture approximative et déclarative (l'en-tête est fourni par le client) : à titre indicatif seulement.
/// </summary>
public sealed partial record UserAgentInfo(string Browser, string OperatingSystem, DeviceKind Device)
{
    public const string UnknownLabel = "Inconnu";

    // Ordre significatif : Edge, Opera et Samsung Internet s'annoncent aussi comme Chrome et Safari.
    private static readonly (string Token, string Name)[] Browsers =
    [
        ("EdgA/", "Edge"),
        ("EdgiOS/", "Edge"),
        ("Edg/", "Edge"),
        ("OPR/", "Opera"),
        ("SamsungBrowser/", "Samsung Internet"),
        ("FxiOS/", "Firefox"),
        ("Firefox/", "Firefox"),
        ("CriOS/", "Chrome"),
        ("Chrome/", "Chrome"),
        ("Chromium/", "Chromium"),
        ("curl/", "curl"),
    ];

    public static UserAgentInfo Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new UserAgentInfo(UnknownLabel, UnknownLabel, DeviceKind.Unknown);
        }
        return new UserAgentInfo(ParseBrowser(userAgent), ParseOperatingSystem(userAgent), ParseDevice(userAgent));
    }

    /// <summary>« Firefox 133 · Windows (ordinateur) ».</summary>
    public string Describe() => Device switch
    {
        DeviceKind.Desktop => $"{Browser} · {OperatingSystem} (ordinateur)",
        DeviceKind.Mobile => $"{Browser} · {OperatingSystem} (mobile)",
        DeviceKind.Tablet => $"{Browser} · {OperatingSystem} (tablette)",
        _ => $"{Browser} · {OperatingSystem}",
    };

    private static string ParseBrowser(string ua)
    {
        foreach (var (token, name) in Browsers)
        {
            var index = ua.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0)
            {
                return WithVersion(name, ua, index + token.Length);
            }
        }
        if (ua.Contains("Safari/", StringComparison.Ordinal))
        {
            // Safari donne sa version dans « Version/17.5 ».
            var version = ua.IndexOf("Version/", StringComparison.Ordinal);
            return version >= 0 ? WithVersion("Safari", ua, version + "Version/".Length) : "Safari";
        }
        return UnknownLabel;
    }

    private static string WithVersion(string name, string ua, int start)
    {
        var end = start;
        while (end < ua.Length && char.IsAsciiDigit(ua[end]))
        {
            end++;
        }
        return end > start ? $"{name} {ua[start..end]}" : name;
    }

    private static string ParseOperatingSystem(string ua)
    {
        if (ua.Contains("Windows", StringComparison.Ordinal))
        {
            return "Windows";
        }
        if (ua.Contains("iPad", StringComparison.Ordinal))
        {
            return Versioned("iPadOS", IosVersion().Match(ua));
        }
        if (ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPod", StringComparison.Ordinal))
        {
            return Versioned("iOS", IosVersion().Match(ua));
        }
        if (ua.Contains("Android", StringComparison.Ordinal))
        {
            return Versioned("Android", AndroidVersion().Match(ua));
        }
        if (ua.Contains("CrOS", StringComparison.Ordinal))
        {
            return "ChromeOS";
        }
        if (ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal))
        {
            // Version figée à 10.15 par les navigateurs : elle n'est pas affichée.
            return "macOS";
        }
        if (ua.Contains("Linux", StringComparison.Ordinal) || ua.Contains("X11", StringComparison.Ordinal))
        {
            return "Linux";
        }
        return UnknownLabel;

        static string Versioned(string name, Match match) => match.Success ? $"{name} {match.Groups[1].Value}" : name;
    }

    private static DeviceKind ParseDevice(string ua)
    {
        if (ua.Contains("iPad", StringComparison.Ordinal) || ua.Contains("Tablet", StringComparison.Ordinal))
        {
            return DeviceKind.Tablet;
        }
        if (ua.Contains("Android", StringComparison.Ordinal))
        {
            // Les tablettes Android n'annoncent pas « Mobile ».
            return ua.Contains("Mobile", StringComparison.Ordinal) ? DeviceKind.Mobile : DeviceKind.Tablet;
        }
        if (ua.Contains("Mobi", StringComparison.Ordinal) || ua.Contains("iPhone", StringComparison.Ordinal))
        {
            return DeviceKind.Mobile;
        }
        if (ua.Contains("Windows", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal)
            || ua.Contains("X11", StringComparison.Ordinal) || ua.Contains("CrOS", StringComparison.Ordinal))
        {
            return DeviceKind.Desktop;
        }
        return DeviceKind.Unknown;
    }

    [GeneratedRegex(@"OS (\d+)[_.]\d", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IosVersion();

    [GeneratedRegex(@"Android (\d+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AndroidVersion();
}
