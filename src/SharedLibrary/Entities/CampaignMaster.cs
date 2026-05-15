using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.Shared.Entities;

[Table("CampaignMaster")]
public class CampaignMaster
{
    [Key]
    public long CampaignId { get; set; }

    [Required, MaxLength(64)]
    public string CampaignCode { get; set; } = default!;

    [Required, MaxLength(256)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    // Picks the Razor view: "Index" | "Enforcement" | "Combined" | "CivicDuty".
    [Required, MaxLength(64)]
    public string PageTemplate { get; set; } = "Index";

    [MaxLength(2048)]
    public string? VideoUrl { get; set; }

    [MaxLength(2048)]
    public string? RegistrationUrl { get; set; }

    [MaxLength(2048)]
    public string? FilingUrl { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Active;

    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? EndDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<TaxpayerRecipient> Recipients { get; set; } = new List<TaxpayerRecipient>();
}
