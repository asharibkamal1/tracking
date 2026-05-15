using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.LandingPortal.Models;
using TaxpayerAnalytics.Shared.Configuration;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;
using TaxpayerAnalytics.Shared.Security;

namespace TaxpayerAnalytics.LandingPortal.Controllers;

[Route("c")]
public sealed class CampaignController(
    AnalyticsDbContext db,
    ITrackingTokenService tokens,
    IOptions<TrackingOptions> trackingOpt,
    ILogger<CampaignController> logger) : Controller
{
    private static readonly HashSet<string> KnownTemplates =
        new(StringComparer.OrdinalIgnoreCase) { "Index", "Enforcement", "Combined", "CivicDuty" };

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery(Name = "t")] string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return View("Invalid", new InvalidPageModel("missing_token"));

        if (!tokens.TryValidate(token, out var payload) || payload is null)
            return View("Invalid", new InvalidPageModel("invalid_or_expired_token"));

        var recipient = await db.Recipients
            .AsNoTracking()
            .Include(r => r.Campaign)
            .FirstOrDefaultAsync(r => r.RecipientId == payload.RecipientId, ct);

        if (recipient is null || recipient.Campaign is null)
            return View("Invalid", new InvalidPageModel("recipient_not_found"));
        if (recipient.Campaign.Status != CampaignStatus.Active)
            return View("Invalid", new InvalidPageModel("campaign_inactive"));

        var template = KnownTemplates.Contains(recipient.Campaign.PageTemplate)
            ? recipient.Campaign.PageTemplate
            : "Index";

        var model = new LandingPageViewModel
        {
            Token = token,
            CampaignCode = recipient.Campaign.CampaignCode,
            CampaignName = recipient.Campaign.Name,
            PageTemplate = template,
            VideoUrl = recipient.Campaign.VideoUrl,
            RegistrationUrl = recipient.Campaign.RegistrationUrl,
            FilingUrl = recipient.Campaign.FilingUrl,
            ApiBaseUrl = trackingOpt.Value.ApiBaseUrl
        };

        Response.Headers.CacheControl = "no-store";
        return View(template, model);
    }

    [HttpGet("redirect")]
    public async Task<IActionResult> Redirect(
        [FromQuery(Name = "t")] string? token,
        [FromQuery] string target,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !tokens.TryValidate(token, out var payload) || payload is null)
            return BadRequest();

        var campaign = await db.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CampaignId == payload.CampaignId, ct);
        if (campaign is null) return NotFound();

        var url = target switch
        {
            "register" => campaign.RegistrationUrl,
            "file" => campaign.FilingUrl,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(url)) return NotFound();

        // Tag the outbound URL with the campaign code + recipient id so downstream
        // systems can correlate the click without ever seeing the NTN.
        var joinChar = url.Contains('?') ? '&' : '?';
        var trackedUrl = $"{url}{joinChar}ref={Uri.EscapeDataString(campaign.CampaignCode)}&rid={payload.RecipientId}";

        logger.LogInformation("Outbound redirect campaign={CampaignId} target={Target}", payload.CampaignId, target);
        return Redirect(trackedUrl);
    }
}
