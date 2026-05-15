using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.Shared.Entities;

[Table("UserSession")]
public class UserSession
{
    [Key]
    public Guid SessionId { get; set; } = Guid.NewGuid();

    public long RecipientId { get; set; }
    public TaxpayerRecipient Recipient { get; set; } = default!;

    public long CampaignId { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(128)]
    public string? Country { get; set; }

    [MaxLength(128)]
    public string? City { get; set; }

    [MaxLength(128)]
    public string? Region { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [MaxLength(1024)]
    public string? UserAgent { get; set; }

    [MaxLength(64)]
    public string? Browser { get; set; }

    [MaxLength(64)]
    public string? BrowserVersion { get; set; }

    [MaxLength(64)]
    public string? OperatingSystem { get; set; }

    [MaxLength(64)]
    public string? OsVersion { get; set; }

    public DeviceType DeviceType { get; set; }

    [MaxLength(32)]
    public string? ScreenResolution { get; set; }

    [MaxLength(16)]
    public string? Language { get; set; }

    [MaxLength(64)]
    public string? Timezone { get; set; }

    [MaxLength(2048)]
    public string? Referrer { get; set; }

    public bool IsBot { get; set; }
    public bool IsBounce { get; set; } = true;
    public bool IsEngaged { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;

    public int DurationSeconds { get; set; }
    public int MaxScrollDepth { get; set; }
    public int VideoWatchSeconds { get; set; }
    public int VideoWatchPercent { get; set; }

    public bool RegisterClicked { get; set; }
    public bool FileClicked { get; set; }
    public DateTime? RegisterClickedAt { get; set; }
    public DateTime? FileClickedAt { get; set; }
    public int TimeToFirstInteractionMs { get; set; }

    public ICollection<EventLog> Events { get; set; } = new List<EventLog>();
}
