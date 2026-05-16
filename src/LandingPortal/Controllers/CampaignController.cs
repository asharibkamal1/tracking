using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaxpayerAnalytics.LandingPortal.Models;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.LandingPortal.Controllers;

[Route("c")]
public sealed class CampaignController(
    AnalyticsDbContext db,
    ILogger<CampaignController> logger) : Controller
{
    private static readonly HashSet<string> KnownTemplates =
        new(StringComparer.OrdinalIgnoreCase) { "Index", "Enforcement", "Combined", "CivicDuty" };

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery(Name = "t")] string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return View("Invalid", new InvalidPageModel("missing_token"));

        var recipient = await db.Recipients
            .AsNoTracking()
            .Include(r => r.Campaign)
            .FirstOrDefaultAsync(r => r.TrackingToken == token, ct);

        if (recipient is null || recipient.Campaign is null)
        {
            logger.LogWarning("Unknown tracking token presented at landing page");
            return View("Invalid", new InvalidPageModel("invalid_or_expired_token"));
        }
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
            // Same-origin: tracking.js will call relative URLs.
            ApiBaseUrl = string.Empty
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
        if (string.IsNullOrWhiteSpace(token)) return BadRequest();

        var recipient = await db.Recipients
            .AsNoTracking()
            .Include(r => r.Campaign)
            .FirstOrDefaultAsync(r => r.TrackingToken == token, ct);
        if (recipient?.Campaign is null) return NotFound();

        var url = target switch
        {
            "register" => recipient.Campaign.RegistrationUrl,
            "file" => recipient.Campaign.FilingUrl,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(url)) return NotFound();

        // Tag the outbound URL with the campaign code + recipient id so downstream
        // systems can correlate the click without ever seeing the NTN.
        var joinChar = url.Contains('?') ? '&' : '?';
        var trackedUrl = $"{url}{joinChar}ref={Uri.EscapeDataString(recipient.Campaign.CampaignCode)}&rid={recipient.RecipientId}";

        logger.LogInformation("Outbound redirect campaign={CampaignId} recipient={Rid} target={Target}",
            recipient.CampaignId, recipient.RecipientId, target);
        return Redirect(trackedUrl);
    }
}
