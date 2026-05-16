using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.LandingPortal.Repositories;
using TaxpayerAnalytics.LandingPortal.Services;
using TaxpayerAnalytics.Shared.Dtos;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.LandingPortal.Controllers;

[ApiController]
[Route("api/v1/events")]
public sealed class EventsController(
    IEventIngestionQueue queue,
    IEventRepository events,
    ILogger<EventsController> logger) : ControllerBase
{
    [HttpPost("batch")]
    [Consumes("application/json")]
    public async Task<ActionResult<TrackResponse>> TrackBatch(
        [FromBody] TrackBatchRequest req,
        CancellationToken ct)
    {
        if (req.Events.Count == 0) return Ok(new TrackResponse { Accepted = 0 });
        if (req.Events.Count > 200) return BadRequest(new { error = "batch_too_large" });

        var sessionId = req.Events[0].SessionId;
        if (req.Events.Any(e => e.SessionId != sessionId))
            return BadRequest(new { error = "mixed_sessions" });

        var session = await events.GetSessionAsync(sessionId, ct);
        if (session is null) return NotFound(new { error = "session_not_found" });
        if (session.IsBot) return Ok(new TrackResponse { Accepted = 0, Rejected = req.Events.Count });

        int accepted = 0, rejected = 0;
        foreach (var dto in req.Events)
        {
            var evt = new EventLog
            {
                SessionId = session.SessionId,
                RecipientId = session.RecipientId,
                CampaignId = session.CampaignId,
                EventType = dto.EventType,
                EventTime = dto.ClientEventTime?.ToUniversalTime() ?? DateTime.UtcNow,
                EventValue = dto.EventValue,
                DurationSeconds = dto.DurationSeconds,
                ScrollDepth = dto.ScrollDepth,
                PageUrl = dto.PageUrl,
                ClientEventId = dto.ClientEventId,
                ExtraJson = dto.Extra is null ? null : JsonSerializer.Serialize(dto.Extra)
            };

            if (queue.TryEnqueue(evt)) accepted++; else rejected++;
        }

        await events.UpdateSessionMetricsAsync(sessionId, s =>
        {
            foreach (var dto in req.Events)
            {
                if (dto.ScrollDepth is int sd && sd > s.MaxScrollDepth) s.MaxScrollDepth = sd;

                if (dto.EventType == EventType.RegisterClick)
                {
                    s.RegisterClicked = true;
                    s.RegisterClickedAt ??= dto.ClientEventTime?.ToUniversalTime() ?? DateTime.UtcNow;
                }
                if (dto.EventType == EventType.FileClick)
                {
                    s.FileClicked = true;
                    s.FileClickedAt ??= dto.ClientEventTime?.ToUniversalTime() ?? DateTime.UtcNow;
                }

                if (dto.EventType is EventType.RegisterClick or EventType.FileClick or EventType.CtaClick)
                {
                    s.IsBounce = false;
                    // Capture how long the visitor stared at the page before their first
                    // meaningful click. Only set once — subsequent CTA clicks don't overwrite.
                    if (s.TimeToFirstInteractionMs == 0)
                    {
                        var tti = ExtractInt(dto.Extra, "timeToInteractMs");
                        if (tti is int t && t > 0) s.TimeToFirstInteractionMs = t;
                    }
                }

                if (dto.EventType == EventType.VideoProgress)
                {
                    if (dto.DurationSeconds is int vw && vw > s.VideoWatchSeconds) s.VideoWatchSeconds = vw;
                    var pct = ExtractInt(dto.Extra, "percent");
                    if (pct is int p && p > s.VideoWatchPercent) s.VideoWatchPercent = Math.Min(100, p);
                }

                if (dto.EventType == EventType.VideoComplete)
                {
                    s.IsBounce = false;
                    if (dto.DurationSeconds is int vc && vc > s.VideoWatchSeconds) s.VideoWatchSeconds = vc;
                    s.VideoWatchPercent = 100;
                }

                if (dto.EventType == EventType.PageClose)
                {
                    if (dto.DurationSeconds is int dur && dur > s.DurationSeconds) s.DurationSeconds = dur;
                    s.EndedAt = dto.ClientEventTime?.ToUniversalTime() ?? DateTime.UtcNow;
                }
            }
            s.LastHeartbeatAt = DateTime.UtcNow;
            s.IsEngaged = s.DurationSeconds >= 15 || s.MaxScrollDepth >= 50
                          || s.RegisterClicked || s.FileClicked || s.VideoWatchPercent >= 25;
        }, ct);

        logger.LogDebug("Batch session={Sid} accepted={A} rejected={R}", sessionId, accepted, rejected);
        return Ok(new TrackResponse { Accepted = accepted, Rejected = rejected });
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        var session = await events.GetSessionAsync(req.SessionId, ct);
        if (session is null) return NotFound(new { error = "session_not_found" });
        if (session.IsBot) return NoContent();

        await events.UpdateSessionMetricsAsync(req.SessionId, s =>
        {
            s.LastHeartbeatAt = DateTime.UtcNow;
            s.DurationSeconds = Math.Max(s.DurationSeconds, req.DurationSeconds);
            s.MaxScrollDepth = Math.Max(s.MaxScrollDepth, req.MaxScrollDepth);
            s.VideoWatchSeconds = Math.Max(s.VideoWatchSeconds, req.VideoWatchSeconds);
            s.IsEngaged = s.DurationSeconds >= 15 || s.MaxScrollDepth >= 50
                          || s.RegisterClicked || s.FileClicked || s.VideoWatchPercent >= 25;
        }, ct);

        queue.TryEnqueue(new EventLog
        {
            SessionId = req.SessionId,
            RecipientId = session.RecipientId,
            CampaignId = session.CampaignId,
            EventType = EventType.Heartbeat,
            EventTime = DateTime.UtcNow,
            DurationSeconds = req.DurationSeconds,
            ScrollDepth = req.MaxScrollDepth
        });

        return NoContent();
    }

    // System.Text.Json deserialises Dictionary<string, object?> values as JsonElement,
    // so we can't just cast — peek the kind and pull the number out.
    private static int? ExtractInt(IDictionary<string, object?>? extra, string key)
    {
        if (extra is null || !extra.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
        {
            if (je.TryGetInt32(out var n)) return n;
            if (je.TryGetDouble(out var d)) return (int)d;
        }
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (v is double dv) return (int)dv;
        return null;
    }
}
