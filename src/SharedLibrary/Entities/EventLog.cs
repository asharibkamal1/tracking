using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.Shared.Entities;

/// <summary>
/// Append-only event store. The composite key (EventId, EventTime) lets us put this
/// table on a monthly partition function in SQL Server (see db/partitioning.sql).
/// </summary>
[Table("EventLog")]
public class EventLog
{
    [Key, Column(Order = 0)]
    public long EventId { get; set; }

    public Guid SessionId { get; set; }
    public UserSession Session { get; set; } = default!;

    public long RecipientId { get; set; }
    public long CampaignId { get; set; }

    public EventType EventType { get; set; }

    /// <summary>
    /// Human-readable name of <see cref="EventType"/> (e.g. "PageOpen", "VideoComplete").
    /// Populated automatically by <see cref="EventLogNameInterceptor"/> so SQL queries
    /// don't have to translate integer codes mentally.
    /// </summary>
    [MaxLength(32)]
    public string? EventTypeName { get; set; }

    [Column(Order = 1)]
    public DateTime EventTime { get; set; } = DateTime.UtcNow;

    [MaxLength(512)]
    public string? EventValue { get; set; }

    public int? DurationSeconds { get; set; }
    public int? ScrollDepth { get; set; }

    [MaxLength(2048)]
    public string? PageUrl { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? ExtraJson { get; set; }

    // Client-generated id so we can de-dupe a retried batch from the SDK.
    [MaxLength(64)]
    public string? ClientEventId { get; set; }
}
