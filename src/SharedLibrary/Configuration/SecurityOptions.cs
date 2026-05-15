namespace TaxpayerAnalytics.Shared.Configuration;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string TokenEncryptionKey { get; set; } = default!;
    public string PiiEncryptionKey { get; set; } = default!;
    public string NtnHashPepper { get; set; } = default!;
    public int RateLimitPerMinute { get; set; } = 120;
    public int SessionMaxIdleSeconds { get; set; } = 600;
}

public sealed class GeoIpOptions
{
    public const string SectionName = "GeoIp";
    public string DbPath { get; set; } = default!;
    public bool Enabled { get; set; } = false;
}

public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";
    public string ApiBaseUrl { get; set; } = default!;
}
