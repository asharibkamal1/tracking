using System.Text.RegularExpressions;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.Shared.Security;

public interface IUserAgentParser
{
    UserAgentInfo Parse(string? userAgent);
}

public sealed record UserAgentInfo(
    string Browser,
    string BrowserVersion,
    string OperatingSystem,
    string OsVersion,
    DeviceType DeviceType);

public sealed class UserAgentParser : IUserAgentParser
{
    public UserAgentInfo Parse(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua))
            return new UserAgentInfo("Unknown", "", "Unknown", "", DeviceType.Unknown);

        var browser = "Unknown"; var browserVersion = "";
        var os = "Unknown"; var osVersion = "";

        if (Matches(ua, @"Edg/([\d.]+)", out var v)) { browser = "Edge"; browserVersion = v; }
        else if (Matches(ua, @"OPR/([\d.]+)", out v) || Matches(ua, @"Opera/([\d.]+)", out v)) { browser = "Opera"; browserVersion = v; }
        else if (Matches(ua, @"Chrome/([\d.]+)", out v)) { browser = "Chrome"; browserVersion = v; }
        else if (Matches(ua, @"Firefox/([\d.]+)", out v)) { browser = "Firefox"; browserVersion = v; }
        else if (Matches(ua, @"Version/([\d.]+).*Safari", out v)) { browser = "Safari"; browserVersion = v; }

        if (ua.Contains("Windows NT", StringComparison.Ordinal)) { os = "Windows"; Matches(ua, @"Windows NT ([\d.]+)", out osVersion); }
        else if (ua.Contains("Android", StringComparison.Ordinal)) { os = "Android"; Matches(ua, @"Android ([\d.]+)", out osVersion); }
        else if (ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal)) { os = "iOS"; Matches(ua, @"OS ([\d_]+)", out osVersion); osVersion = osVersion.Replace('_', '.'); }
        else if (ua.Contains("Mac OS X", StringComparison.Ordinal)) { os = "macOS"; Matches(ua, @"Mac OS X ([\d_]+)", out osVersion); osVersion = osVersion.Replace('_', '.'); }
        else if (ua.Contains("Linux", StringComparison.Ordinal)) { os = "Linux"; }

        var device = DeviceType.Desktop;
        if (Regex.IsMatch(ua, @"Mobi|iPhone|Android.*Mobile", RegexOptions.IgnoreCase)) device = DeviceType.Mobile;
        else if (Regex.IsMatch(ua, @"iPad|Tablet", RegexOptions.IgnoreCase)) device = DeviceType.Tablet;

        return new UserAgentInfo(browser, browserVersion, os, osVersion, device);
    }

    private static bool Matches(string input, string pattern, out string capture)
    {
        var m = Regex.Match(input, pattern);
        capture = m.Success && m.Groups.Count > 1 ? m.Groups[1].Value : string.Empty;
        return m.Success;
    }
}
