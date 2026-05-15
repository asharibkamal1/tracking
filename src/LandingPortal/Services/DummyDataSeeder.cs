using Microsoft.EntityFrameworkCore;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;
using TaxpayerAnalytics.Shared.Security;

namespace TaxpayerAnalytics.LandingPortal.Services;

public interface IDummyDataSeeder
{
    Task<IReadOnlyList<DummyRecipientInfo>> EnsureSeededAsync(CancellationToken ct);
}

public sealed record DummyRecipientInfo(
    string CampaignCode,
    string CampaignName,
    string PageTemplate,
    string MaskedNtn,
    string TrackingToken,
    string LandingUrl);

/// <summary>
/// Idempotently creates one dummy TaxpayerRecipient per seeded campaign so we can
/// click through to each landing page during local testing. The 4 dummy NTNs are
/// clearly distinguishable (1111111-1, 2222222-2, ...) and the masked form is what
/// the test launcher displays.
/// </summary>
public sealed class DummyDataSeeder(
    AnalyticsDbContext db,
    ITrackingTokenService tokens,
    IPiiCipher cipher,
    IHttpContextAccessor httpCtx,
    ILogger<DummyDataSeeder> logger) : IDummyDataSeeder
{
    private static readonly (string Code, string Ntn, string Mobile)[] Fixtures =
    {
        ("GENERAL",     "1111111-1", "03001111111"),
        ("ENFORCEMENT", "2222222-2", "03002222222"),
        ("COMBINED",    "3333333-3", "03003333333"),
        ("CIVIC",       "4444444-4", "03004444444")
    };

    public async Task<IReadOnlyList<DummyRecipientInfo>> EnsureSeededAsync(CancellationToken ct)
    {
        var campaigns = await db.Campaigns
            .Where(c => c.Status == CampaignStatus.Active)
            .ToDictionaryAsync(c => c.CampaignCode, c => c, ct);

        var results = new List<DummyRecipientInfo>(Fixtures.Length);

        foreach (var (code, ntn, mobile) in Fixtures)
        {
            if (!campaigns.TryGetValue(code, out var campaign))
            {
                logger.LogWarning("Seed campaign {Code} missing — run db/schema.sql first", code);
                continue;
            }

            var ntnHash = tokens.HashNtn(ntn);
            var recipient = await db.Recipients
                .FirstOrDefaultAsync(r => r.CampaignId == campaign.CampaignId && r.NtnHash == ntnHash, ct);

            if (recipient is null)
            {
                recipient = new TaxpayerRecipient
                {
                    CampaignId = campaign.CampaignId,
                    NtnHash = ntnHash,
                    NtnEncrypted = cipher.Encrypt(ntn),
                    MaskedNtn = cipher.MaskNtn(ntn),
                    MobileEncrypted = cipher.Encrypt(mobile),
                    MobileMasked = cipher.MaskMobile(mobile),
                    Language = "en",
                    // Placeholder; we overwrite with a real token once we have the id.
                    TrackingToken = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    SmsSentAt = DateTime.UtcNow
                };
                db.Recipients.Add(recipient);
                await db.SaveChangesAsync(ct);

                recipient.TrackingToken = tokens.Issue(recipient.RecipientId, recipient.CampaignId);
                await db.SaveChangesAsync(ct);
            }
            else if (string.IsNullOrEmpty(recipient.TrackingToken) || recipient.TrackingToken.Length < 50)
            {
                // Heal old rows where the token wasn't a proper AES-GCM payload.
                recipient.TrackingToken = tokens.Issue(recipient.RecipientId, recipient.CampaignId);
                await db.SaveChangesAsync(ct);
            }

            var landing = BuildLandingUrl(recipient.TrackingToken);
            results.Add(new DummyRecipientInfo(
                campaign.CampaignCode,
                campaign.Name,
                campaign.PageTemplate,
                recipient.MaskedNtn ?? "****",
                recipient.TrackingToken,
                landing));
        }

        return results;
    }

    private string BuildLandingUrl(string token)
    {
        var req = httpCtx.HttpContext?.Request;
        var host = req is null
            ? "https://localhost:5001"
            : $"{req.Scheme}://{req.Host}";
        return $"{host}/c?t={Uri.EscapeDataString(token)}";
    }
}
