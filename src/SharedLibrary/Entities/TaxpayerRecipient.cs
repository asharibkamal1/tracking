using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaxpayerAnalytics.Shared.Entities;

/// <summary>
/// Row populated by your existing SMS pipeline. We never expose NTN in the URL —
/// only TrackingToken (an encrypted AES-GCM payload that carries RecipientId + CampaignId).
/// </summary>
[Table("TaxpayerRecipient")]
public class TaxpayerRecipient
{
    [Key]
    public long RecipientId { get; set; }

    public long CampaignId { get; set; }
    public CampaignMaster Campaign { get; set; } = default!;

    // HMAC-SHA256(NTN, pepper). Deterministic so the SMS pipeline can de-dupe / look up
    // without ever decrypting the raw NTN.
    [Required, MaxLength(128)]
    public string NtnHash { get; set; } = default!;

    // AES-GCM ciphertext of the raw NTN. Decrypt only on the secure dashboard side.
    [Required]
    public byte[] NtnEncrypted { get; set; } = default!;

    [MaxLength(32)]
    public string? MaskedNtn { get; set; }

    [MaxLength(32)]
    public string? MobileMasked { get; set; }

    [Required]
    public byte[] MobileEncrypted { get; set; } = default!;

    [MaxLength(8)]
    public string? Language { get; set; } = "en";

    [Required, MaxLength(512)]
    public string TrackingToken { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SmsSentAt { get; set; }

    public DateTime? FirstVisitAt { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public int VisitCount { get; set; }

    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}
