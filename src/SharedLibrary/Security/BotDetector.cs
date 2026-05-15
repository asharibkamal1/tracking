using System.Text.RegularExpressions;

namespace TaxpayerAnalytics.Shared.Security;

public interface IBotDetector
{
    BotDetectionResult Inspect(string? userAgent, string? acceptLanguage, IDictionary<string, string?> headers);
}

public sealed record BotDetectionResult(bool IsBot, string? Reason);

public sealed partial class BotDetector : IBotDetector
{
    private static readonly Regex BotUaRegex = MyRegex();

    public BotDetectionResult Inspect(string? userAgent, string? acceptLanguage, IDictionary<string, string?> headers)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return new BotDetectionResult(true, "missing user-agent");

        if (BotUaRegex.IsMatch(userAgent))
            return new BotDetectionResult(true, "known bot user-agent");

        if (userAgent.Contains("HeadlessChrome", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("PhantomJS", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Electron", StringComparison.OrdinalIgnoreCase))
            return new BotDetectionResult(true, "headless browser");

        if (string.IsNullOrWhiteSpace(acceptLanguage))
            return new BotDetectionResult(true, "missing accept-language");

        if (!headers.ContainsKey("Accept-Encoding"))
            return new BotDetectionResult(true, "missing accept-encoding");

        return new BotDetectionResult(false, null);
    }

    [GeneratedRegex(@"bot|crawl|spider|slurp|bing|google|yandex|baidu|duckduck|facebookexternalhit|http\-client|curl|wget|python\-requests|axios|java/|go-http-client",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
