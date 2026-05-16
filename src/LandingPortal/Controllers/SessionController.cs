using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.LandingPortal.Repositories;
using TaxpayerAnalytics.LandingPortal.Services;
using TaxpayerAnalytics.Shared.Dtos;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;
using TaxpayerAnalytics.Shared.Security;

namespace TaxpayerAnalytics.LandingPortal.Controllers;

[ApiController]
[Route("api/v1/session")]
public sealed class SessionController(
    ISessionRepository sessions,
    IGeoIpService geo,
    IUserAgentParser uaParser,
    IBotDetector bot,
    IEventIngestionQueue queue,
    ILogger<SessionController> logger) : ControllerBase
{
    [HttpPost("start")]
    public async Task<ActionResult<StartSessionResponse>> Start(
        [FromBody] StartSessionRequest req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(req.Token))
            return Unauthorized(new { error = "missing_token" });

        var recipient = await sessions.GetRecipientByTokenAsync(req.Token, ct);
        if (recipient is null)
        {
            logger.LogWarning("Unknown tracking token presented (start)");
            return Unauthorized(new { error = "invalid_token" });
        }

        var ua = Request.Headers.UserAgent.ToString();
        var headers = Request.Headers.ToDictionary(h => h.Key, h => (string?)h.Value.ToString());
        var botResult = bot.Inspect(ua, Request.Headers.AcceptLanguage, headers);
        var uaInfo = uaParser.Parse(ua);
        var ip = ResolveClientIp();
        var geoInfo = geo.Lookup(ip);

        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            SessionId = Guid.NewGuid(),
            RecipientId = recipient.RecipientId,
            CampaignId = recipient.CampaignId,
            IpAddress = ip,
            Country = geoInfo.Country,
            City = geoInfo.City,
            Region = geoInfo.Region,
            Latitude = geoInfo.Latitude,
            Longitude = geoInfo.Longitude,
            UserAgent = ua,
            Browser = uaInfo.Browser,
            BrowserVersion = uaInfo.BrowserVersion,
            OperatingSystem = uaInfo.OperatingSystem,
            OsVersion = uaInfo.OsVersion,
            DeviceType = botResult.IsBot ? DeviceType.Bot : uaInfo.DeviceType,
            ScreenResolution = req.ScreenResolution,
            Language = req.Language ?? recipient.Language,
            Timezone = req.Timezone,
            Referrer = req.Referrer,
            IsBot = botResult.IsBot,
            StartedAt = now,
            LastHeartbeatAt = now
        };

        await sessions.CreateSessionAsync(session, ct);
        await sessions.IncrementVisitAsync(recipient.RecipientId, now, ct);

        queue.TryEnqueue(new EventLog
        {
            SessionId = session.SessionId,
            RecipientId = recipient.RecipientId,
            CampaignId = recipient.CampaignId,
            EventType = botResult.IsBot ? EventType.BotDetected : EventType.PageOpen,
            EventTime = now,
            PageUrl = req.PageUrl,
            EventValue = botResult.Reason
        });

        logger.LogInformation(
            "Session {Sid} started recipient={Rid} campaign={Cid} bot={Bot} browser={Br} device={Dev}",
            session.SessionId, recipient.RecipientId, recipient.CampaignId,
            botResult.IsBot, uaInfo.Browser, uaInfo.DeviceType);

        return Ok(new StartSessionResponse
        {
            SessionId = session.SessionId,
            CampaignId = session.CampaignId,
            IsBot = botResult.IsBot
        });
    }

    private string? ResolveClientIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
        {
            var first = xff.ToString().Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
