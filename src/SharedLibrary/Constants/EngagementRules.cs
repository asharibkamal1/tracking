namespace TaxpayerAnalytics.Shared.Constants;

/// <summary>
/// Centralised thresholds for behavioural metrics. Edit values here once instead
/// of hunting through controllers and dashboards.
/// </summary>
public static class EngagementRules
{
    /// <summary>Seconds a visitor must stay before we consider the session "engaged" on duration alone.</summary>
    public const int EngagedDurationSeconds = 15;

    /// <summary>Scroll-depth percentage that flips a session to "engaged" regardless of duration.</summary>
    public const int EngagedScrollPercent = 50;

    /// <summary>Video-watch percentage above which a session is considered engaged.</summary>
    public const int EngagedVideoWatchPercent = 25;
}

public static class TrackingLimits
{
    /// <summary>Hard cap on events accepted per /events/batch request.</summary>
    public const int MaxEventsPerBatch = 200;

    /// <summary>Hard cap on rows returned from the taxpayer-list endpoint.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Floor for paged dashboard queries.</summary>
    public const int MinPageSize = 10;

    /// <summary>Sessions whose LastHeartbeatAt is within this many seconds count as "active now".</summary>
    public const int ActiveUserWindowSeconds = 30;

    /// <summary>Interval at which ActiveUsersBroadcaster pushes per-campaign counts.</summary>
    public const int ActiveUsersBroadcastSeconds = 5;
}
