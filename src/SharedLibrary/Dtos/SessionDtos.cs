using System.ComponentModel.DataAnnotations;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.Shared.Dtos;

public sealed class StartSessionRequest
{
    [Required, MaxLength(2048)] public string Token { get; set; } = default!;
    [MaxLength(32)] public string? ScreenResolution { get; set; }
    [MaxLength(16)] public string? Language { get; set; }
    [MaxLength(64)] public string? Timezone { get; set; }
    [MaxLength(2048)] public string? Referrer { get; set; }
    [MaxLength(2048)] public string? PageUrl { get; set; }
}

public sealed class StartSessionResponse
{
    public Guid SessionId { get; set; }
    public long CampaignId { get; set; }
    public int HeartbeatSeconds { get; set; } = 10;
    public bool IsBot { get; set; }
}

public sealed class TrackEventDto
{
    [Required] public Guid SessionId { get; set; }
    [Required] public EventType EventType { get; set; }
    [MaxLength(64)] public string? ClientEventId { get; set; }
    public DateTime? ClientEventTime { get; set; }
    [MaxLength(512)] public string? EventValue { get; set; }
    public int? DurationSeconds { get; set; }
    public int? ScrollDepth { get; set; }
    [MaxLength(2048)] public string? PageUrl { get; set; }
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class TrackBatchRequest
{
    [Required] public List<TrackEventDto> Events { get; set; } = new();
}

public sealed class TrackResponse
{
    public int Accepted { get; set; }
    public int Rejected { get; set; }
}

public sealed class HeartbeatRequest
{
    [Required] public Guid SessionId { get; set; }
    public int DurationSeconds { get; set; }
    public int MaxScrollDepth { get; set; }
    public int VideoWatchSeconds { get; set; }
}
