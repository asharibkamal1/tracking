using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.Shared.Dtos;

public sealed class DashboardQuery
{
    public long? CampaignId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool ExcludeBots { get; set; } = true;
}

public sealed class CampaignOverviewDto
{
    public long CampaignId { get; set; }
    public string CampaignCode { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string PageTemplate { get; set; } = default!;
    public int TotalRecipients { get; set; }
    public int TotalSmsSent { get; set; }
    public int TotalVisits { get; set; }
    public int UniqueVisitors { get; set; }
    public int ReturnVisits { get; set; }
    public double ClickThroughRatePct { get; set; }
    public double AvgSessionDurationSec { get; set; }
    public double AvgScrollDepthPct { get; set; }
    public int RegisterClicks { get; set; }
    public int FileClicks { get; set; }
    public double BounceRatePct { get; set; }
    public int VideoStarts { get; set; }
    public int VideoCompletions { get; set; }
    public double VideoAvgWatchPct { get; set; }
    public int ActiveUsersNow { get; set; }
    public int EngagedVisitors { get; set; }
}

public sealed class TimeSeriesPointDto
{
    public DateTime Bucket { get; set; }
    public int Value { get; set; }
}

public sealed class DeviceBreakdownDto
{
    public string DeviceType { get; set; } = default!;
    public int Sessions { get; set; }
    public double Percent { get; set; }
}

public sealed class GeoBreakdownDto
{
    public string? Country { get; set; }
    public string? City { get; set; }
    public int Sessions { get; set; }
}

public sealed class TaxpayerRowDto
{
    public long RecipientId { get; set; }
    public string CampaignCode { get; set; } = default!;
    public string MaskedNtn { get; set; } = default!;
    public string? MobileMasked { get; set; }
    public DateTime? FirstVisitAt { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public int VisitCount { get; set; }
    public int TotalDurationSec { get; set; }
    public int MaxScrollDepth { get; set; }
    public int VideoWatchPercent { get; set; }
    public bool RegisterClicked { get; set; }
    public bool FileClicked { get; set; }
    public bool EverBot { get; set; }
}

public sealed class TaxpayerDetailDto
{
    public long RecipientId { get; set; }
    public string CampaignCode { get; set; } = default!;
    public string CampaignName { get; set; } = default!;
    public string MaskedNtn { get; set; } = default!;
    public string? MobileMasked { get; set; }
    public string? Language { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SmsSentAt { get; set; }
    public DateTime? FirstVisitAt { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public int VisitCount { get; set; }
    public List<TaxpayerSessionDto> Sessions { get; set; } = new();
}

public sealed class TaxpayerSessionDto
{
    public Guid SessionId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public int DurationSeconds { get; set; }
    public int MaxScrollDepth { get; set; }
    public int VideoWatchSeconds { get; set; }
    public int VideoWatchPercent { get; set; }
    public bool RegisterClicked { get; set; }
    public bool FileClicked { get; set; }
    public int TimeToFirstInteractionMs { get; set; }
    public bool IsBot { get; set; }
    public bool IsBounce { get; set; }
    public bool IsEngaged { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public DeviceType DeviceType { get; set; }
    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? ScreenResolution { get; set; }
    public string? Referrer { get; set; }
    public List<TimelineEventDto> Events { get; set; } = new();
}

public sealed class TimelineEventDto
{
    public long EventId { get; set; }
    public DateTime EventTime { get; set; }
    public EventType EventType { get; set; }
    public string EventTypeName { get; set; } = default!;
    public string? EventValue { get; set; }
    public int? DurationSeconds { get; set; }
    public int? ScrollDepth { get; set; }
    public string? PageUrl { get; set; }
}

public sealed class LiveEventDto
{
    public string Kind { get; set; } = default!;          // "session" | "event"
    public DateTime At { get; set; }
    public Guid SessionId { get; set; }
    public long CampaignId { get; set; }
    public string CampaignCode { get; set; } = default!;
    public long RecipientId { get; set; }
    public string MaskedNtn { get; set; } = default!;
    public string EventTypeName { get; set; } = default!;
    public string? EventValue { get; set; }
    public string? Browser { get; set; }
    public string? DeviceType { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
}

public sealed class PagedResult<T>
{
    public List<T> Rows { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
