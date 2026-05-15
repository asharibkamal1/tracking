using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.Shared.Dtos;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;
using TaxpayerAnalytics.TrackingApi.Repositories;
using TaxpayerAnalytics.TrackingApi.Services;

namespace TaxpayerAnalytics.TrackingApi.Controllers;

[ApiController]
[Route("api/v1/events")]
public sealed class EventsController(
    IEventIngestionQueue queue,
    IEventRepository events) : ControllerBase
{
    [HttpPost("batch")]
    [Consumes("application/json")]
    public async Task<ActionResult<TrackResponse>> TrackBatch(
        [FromBody] TrackBatchRequest req,
        CancellationToken ct)
    {
        if (req.Events.Count == 0) return Ok(new TrackResponse { Accepted = 0 });
        if (req.Events.Count > 200) return BadRequest(new { error = "batch_too_large" });

        // All events in a batch must share a session. Validate up front (one round-trip).
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

        // Best-effort live session aggregates so the dashboard (when we add it) doesn't
        // have to wait for the async event flush to learn about clicks / scroll.
        await events.UpdateSessionMetricsAsync(sessionId, s =>
        {
            foreach (var dto in req.Events)
            {
                if (dto.ScrollDepth is int sd && sd > s.MaxScrollDepth) s.MaxScrollDepth = sd;
                if (dto.EventType == EventType.RegisterClick) { s.RegisterClicked = true; s.RegisterClickedAt ??= DateTime.UtcNow; }
                if (dto.EventType == EventType.FileClick) { s.FileClicked = true; s.FileClickedAt ??= DateTime.UtcNow; }
                if (dto.EventType is EventType.RegisterClick or EventType.FileClick or EventType.CtaClick)
                    s.IsBounce = false;
                if (dto.EventType == EventType.VideoProgress && dto.DurationSeconds is int vw && vw > s.VideoWatchSeconds)
                    s.VideoWatchSeconds = vw;
            }
            s.LastHeartbeatAt = DateTime.UtcNow;
        }, ct);

        return Ok(new TrackResponse { Accepted = accepted, Rejected = rejected });
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        await events.UpdateSessionMetricsAsync(req.SessionId, s =>
        {
            s.LastHeartbeatAt = DateTime.UtcNow;
            s.DurationSeconds = Math.Max(s.DurationSeconds, req.DurationSeconds);
            s.MaxScrollDepth = Math.Max(s.MaxScrollDepth, req.MaxScrollDepth);
            s.VideoWatchSeconds = Math.Max(s.VideoWatchSeconds, req.VideoWatchSeconds);
            s.IsEngaged = s.DurationSeconds >= 15 || s.MaxScrollDepth >= 50;
        }, ct);

        queue.TryEnqueue(new EventLog
        {
            SessionId = req.SessionId,
            EventType = EventType.Heartbeat,
            EventTime = DateTime.UtcNow,
            DurationSeconds = req.DurationSeconds,
            ScrollDepth = req.MaxScrollDepth
        });

        return NoContent();
    }
}
