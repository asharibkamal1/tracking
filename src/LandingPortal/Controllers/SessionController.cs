using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.LandingPortal.Repositories;
using TaxpayerAnalytics.LandingPortal.Services;
using TaxpayerAnalytics.LandingPortal.Services.Dashboard;
using TaxpayerAnalytics.Shared.Configuration;
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
    IRealtimeNotifier realtime,
    IOptions<SecurityOptions> securityOpt,
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

        // Session continuation: if this recipient was active within the configured
        // window (refresh / quick reopen), reuse their session instead of inflating
        // VisitCount and ActiveUsers. Bot sessions are never reused so a bot can't
        // hijack a legitimate session.
        if (!botResult.IsBot)
        {
            var window = TimeSpan.FromMinutes(Math.Max(1, securityOpt.Value.SessionContinuationMinutes));
            var existing = await sessions.GetRecentSessionForRecipientAsync(recipient.RecipientId, now - window, ct);
            if (existing is not null)
            {
                await sessions.TouchSessionAsync(existing.SessionId, now, ct);

                queue.TryEnqueue(new EventLog
                {
                    SessionId = existing.SessionId,
                    RecipientId = recipient.RecipientId,
                    CampaignId = recipient.CampaignId,
                    EventType = EventType.PageOpen,
                    EventTime = now,
                    PageUrl = req.PageUrl,
                    EventValue = "continuation"
                });

                logger.LogInformation("Session {Sid} continued for recipient={Rid} (age {Age:F0}s)",
                    existing.SessionId, recipient.RecipientId, (now - existing.StartedAt).TotalSeconds);

                _ = realtime.PushEventAsync(new LiveEventDto
                {
                    Kind = "event",
                    At = now,
                    SessionId = existing.SessionId,
                    CampaignId = recipient.CampaignId,
                    CampaignCode = recipient.Campaign?.CampaignCode ?? string.Empty,
                    RecipientId = recipient.RecipientId,
                    MaskedNtn = recipient.MaskedNtn ?? "****",
                    EventTypeName = nameof(EventType.PageOpen),
                    EventValue = "refresh",
                    Browser = existing.Browser,
                    DeviceType = existing.DeviceType.ToString(),
                    Country = existing.Country,
                    City = existing.City
                });

                return Ok(new StartSessionResponse
                {
                    SessionId = existing.SessionId,
                    CampaignId = existing.CampaignId,
                    IsBot = false
                });
            }
        }

        // GeoIP databases don't carry data for loopback addresses (::1, 127.0.0.1) and
        // most local subnets. Tag those explicitly so the columns aren't NULL in dev —
        // a real production deployment with a configured MaxMind DB will get real geo.
        if (geoInfo.Country is null && IsLocalAddress(ip))
        {
            geoInfo = new Services.GeoLookupResult("Local", "Localhost", "Local", null, null);
        }

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

        if (!botResult.IsBot)
        {
            // Fire-and-forget — failure to push live doesn't fail the request.
            _ = realtime.PushEventAsync(new LiveEventDto
            {
                Kind = "session",
                At = now,
                SessionId = session.SessionId,
                CampaignId = recipient.CampaignId,
                CampaignCode = recipient.Campaign?.CampaignCode ?? string.Empty,
                RecipientId = recipient.RecipientId,
                MaskedNtn = recipient.MaskedNtn ?? "****",
                EventTypeName = nameof(EventType.PageOpen),
                Browser = uaInfo.Browser,
                DeviceType = uaInfo.DeviceType.ToString(),
                Country = geoInfo.Country,
                City = geoInfo.City
            });
        }

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

    private static bool IsLocalAddress(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        return ip is "::1" or "127.0.0.1"
               || ip.StartsWith("10.", StringComparison.Ordinal)
               || ip.StartsWith("192.168.", StringComparison.Ordinal)
               || ip.StartsWith("172.16.", StringComparison.Ordinal)
               || ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase);
    }
}
