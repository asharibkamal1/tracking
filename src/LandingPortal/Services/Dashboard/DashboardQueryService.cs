using Microsoft.EntityFrameworkCore;
using TaxpayerAnalytics.Shared.Constants;
using TaxpayerAnalytics.Shared.Dtos;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.LandingPortal.Services.Dashboard;

public sealed class DashboardQueryService(AnalyticsDbContext db) : IDashboardQueryService
{
    public async Task<List<CampaignOverviewDto>> GetAllCampaignsOverviewAsync(DashboardQuery q, CancellationToken ct)
    {
        var (from, to) = ResolveRange(q);
        var campaigns = await db.Campaigns.AsNoTracking()
            .OrderBy(c => c.CampaignId)
            .ToListAsync(ct);

        var results = new List<CampaignOverviewDto>(campaigns.Count);
        foreach (var c in campaigns)
        {
            var view = await BuildOverviewAsync(c.CampaignId, c.CampaignCode, c.Name, c.PageTemplate, from, to, q.ExcludeBots, ct);
            results.Add(view);
        }
        return results;
    }

    public async Task<CampaignOverviewDto?> GetCampaignOverviewAsync(long campaignId, DashboardQuery q, CancellationToken ct)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.CampaignId == campaignId, ct);
        if (c is null) return null;
        var (from, to) = ResolveRange(q);
        return await BuildOverviewAsync(c.CampaignId, c.CampaignCode, c.Name, c.PageTemplate, from, to, q.ExcludeBots, ct);
    }

    private async Task<CampaignOverviewDto> BuildOverviewAsync(
        long campaignId, string code, string name, string template,
        DateTime from, DateTime to, bool excludeBots, CancellationToken ct)
    {
        var sessions = db.Sessions.AsNoTracking()
            .Where(s => s.CampaignId == campaignId && s.StartedAt >= from && s.StartedAt < to);
        if (excludeBots) sessions = sessions.Where(s => !s.IsBot);

        var recipients = db.Recipients.AsNoTracking().Where(r => r.CampaignId == campaignId);

        var totalRecipients = await recipients.CountAsync(ct);
        var totalSms = await recipients.CountAsync(r => r.SmsSentAt != null, ct);
        var totalVisits = await sessions.CountAsync(ct);
        var uniqueVisitors = await sessions.Select(s => s.RecipientId).Distinct().CountAsync(ct);
        var returnVisits = await recipients.CountAsync(r => r.VisitCount > 1, ct);
        var avgDuration = totalVisits == 0 ? 0 : await sessions.AverageAsync(s => (double)s.DurationSeconds, ct);
        var avgScroll = totalVisits == 0 ? 0 : await sessions.AverageAsync(s => (double)s.MaxScrollDepth, ct);
        var registerClicks = await sessions.CountAsync(s => s.RegisterClicked, ct);
        var fileClicks = await sessions.CountAsync(s => s.FileClicked, ct);
        var bounces = await sessions.CountAsync(s => s.IsBounce, ct);
        var engaged = await sessions.CountAsync(s => s.IsEngaged, ct);
        var avgVideoPct = totalVisits == 0 ? 0 : await sessions.AverageAsync(s => (double)s.VideoWatchPercent, ct);

        var videoStarts = await db.Events.AsNoTracking()
            .CountAsync(e => e.CampaignId == campaignId && e.EventTime >= from && e.EventTime < to && e.EventType == EventType.VideoPlay, ct);
        var videoCompletes = await db.Events.AsNoTracking()
            .CountAsync(e => e.CampaignId == campaignId && e.EventTime >= from && e.EventTime < to && e.EventType == EventType.VideoComplete, ct);

        var ctr = totalSms == 0 ? 0 : 100.0 * uniqueVisitors / totalSms;
        var bouncePct = totalVisits == 0 ? 0 : 100.0 * bounces / totalVisits;

        return new CampaignOverviewDto
        {
            CampaignId = campaignId,
            CampaignCode = code,
            Name = name,
            PageTemplate = template,
            TotalRecipients = totalRecipients,
            TotalSmsSent = totalSms,
            TotalVisits = totalVisits,
            UniqueVisitors = uniqueVisitors,
            ReturnVisits = returnVisits,
            ClickThroughRatePct = Math.Round(ctr, 2),
            AvgSessionDurationSec = Math.Round(avgDuration, 1),
            AvgScrollDepthPct = Math.Round(avgScroll, 1),
            RegisterClicks = registerClicks,
            FileClicks = fileClicks,
            BounceRatePct = Math.Round(bouncePct, 2),
            VideoStarts = videoStarts,
            VideoCompletions = videoCompletes,
            VideoAvgWatchPct = Math.Round(avgVideoPct, 1),
            EngagedVisitors = engaged,
            ActiveUsersNow = await GetActiveUsersAsync(campaignId, ct)
        };
    }

    public async Task<List<TimeSeriesPointDto>> GetVisitsTimeSeriesAsync(DashboardQuery q, string bucket, CancellationToken ct)
    {
        var (from, to) = ResolveRange(q);
        var sessions = db.Sessions.AsNoTracking()
            .Where(s => s.StartedAt >= from && s.StartedAt < to && (q.CampaignId == null || s.CampaignId == q.CampaignId));
        if (q.ExcludeBots) sessions = sessions.Where(s => !s.IsBot);

        if (bucket == "day")
        {
            return await sessions
                .GroupBy(s => new DateTime(s.StartedAt.Year, s.StartedAt.Month, s.StartedAt.Day))
                .OrderBy(g => g.Key)
                .Select(g => new TimeSeriesPointDto { Bucket = g.Key, Value = g.Count() })
                .ToListAsync(ct);
        }
        return await sessions
            .GroupBy(s => new DateTime(s.StartedAt.Year, s.StartedAt.Month, s.StartedAt.Day, s.StartedAt.Hour, 0, 0))
            .OrderBy(g => g.Key)
            .Select(g => new TimeSeriesPointDto { Bucket = g.Key, Value = g.Count() })
            .ToListAsync(ct);
    }

    public async Task<List<DeviceBreakdownDto>> GetDeviceBreakdownAsync(DashboardQuery q, CancellationToken ct)
    {
        var (from, to) = ResolveRange(q);
        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.StartedAt >= from && s.StartedAt < to
                && (q.CampaignId == null || s.CampaignId == q.CampaignId)
                && (!q.ExcludeBots || !s.IsBot))
            .GroupBy(s => s.DeviceType)
            .Select(g => new { Device = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = Math.Max(1, rows.Sum(r => r.Count));
        return rows.Select(r => new DeviceBreakdownDto
        {
            DeviceType = r.Device.ToString(),
            Sessions = r.Count,
            Percent = Math.Round(100.0 * r.Count / total, 2)
        }).ToList();
    }

    public async Task<List<GeoBreakdownDto>> GetGeoBreakdownAsync(DashboardQuery q, int top, CancellationToken ct)
    {
        var (from, to) = ResolveRange(q);
        return await db.Sessions.AsNoTracking()
            .Where(s => s.StartedAt >= from && s.StartedAt < to
                && (q.CampaignId == null || s.CampaignId == q.CampaignId)
                && (!q.ExcludeBots || !s.IsBot))
            .GroupBy(s => new { s.Country, s.City })
            .Select(g => new GeoBreakdownDto { Country = g.Key.Country, City = g.Key.City, Sessions = g.Count() })
            .OrderByDescending(x => x.Sessions)
            .Take(top)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<TaxpayerRowDto>> GetTaxpayerListAsync(
        DashboardQuery q, string? search, int page, int pageSize, CancellationToken ct)
    {
        var (from, to) = ResolveRange(q);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, TrackingLimits.MinPageSize, TrackingLimits.MaxPageSize);

        IQueryable<TaxpayerRecipient> baseQuery = db.Recipients.AsNoTracking().Include(r => r.Campaign);
        if (q.CampaignId.HasValue) baseQuery = baseQuery.Where(r => r.CampaignId == q.CampaignId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            baseQuery = baseQuery.Where(r => r.MaskedNtn!.Contains(search) || r.MobileMasked!.Contains(search));

        var total = await baseQuery.CountAsync(ct);

        // Project once with sub-queries so we don't fan-out N+1 selects.
        var rows = await baseQuery
            .OrderByDescending(r => r.LastVisitAt ?? r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new TaxpayerRowDto
            {
                RecipientId = r.RecipientId,
                CampaignCode = r.Campaign.CampaignCode,
                MaskedNtn = r.MaskedNtn ?? "****",
                MobileMasked = r.MobileMasked,
                FirstVisitAt = r.FirstVisitAt,
                LastVisitAt = r.LastVisitAt,
                VisitCount = r.VisitCount,
                TotalDurationSec = db.Sessions
                    .Where(s => s.RecipientId == r.RecipientId && s.StartedAt >= from && s.StartedAt < to
                        && (!q.ExcludeBots || !s.IsBot))
                    .Sum(s => (int?)s.DurationSeconds) ?? 0,
                MaxScrollDepth = db.Sessions
                    .Where(s => s.RecipientId == r.RecipientId && s.StartedAt >= from && s.StartedAt < to
                        && (!q.ExcludeBots || !s.IsBot))
                    .Max(s => (int?)s.MaxScrollDepth) ?? 0,
                VideoWatchPercent = db.Sessions
                    .Where(s => s.RecipientId == r.RecipientId && s.StartedAt >= from && s.StartedAt < to
                        && (!q.ExcludeBots || !s.IsBot))
                    .Max(s => (int?)s.VideoWatchPercent) ?? 0,
                RegisterClicked = db.Sessions.Any(s => s.RecipientId == r.RecipientId && s.RegisterClicked),
                FileClicked = db.Sessions.Any(s => s.RecipientId == r.RecipientId && s.FileClicked),
                EverBot = db.Sessions.Any(s => s.RecipientId == r.RecipientId && s.IsBot)
            })
            .ToListAsync(ct);

        return new PagedResult<TaxpayerRowDto> { Rows = rows, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task<TaxpayerDetailDto?> GetTaxpayerDetailAsync(long recipientId, CancellationToken ct)
    {
        var r = await db.Recipients.AsNoTracking()
            .Include(x => x.Campaign)
            .FirstOrDefaultAsync(x => x.RecipientId == recipientId, ct);
        if (r is null || r.Campaign is null) return null;

        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.RecipientId == recipientId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

        var sessionIds = sessions.Select(s => s.SessionId).ToList();
        var events = await db.Events.AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId))
            .OrderBy(e => e.EventTime)
            .Select(e => new
            {
                e.EventId, e.SessionId, e.EventTime, e.EventType, e.EventTypeName,
                e.EventValue, e.DurationSeconds, e.ScrollDepth, e.PageUrl
            })
            .ToListAsync(ct);

        var eventsBySession = events.GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => g.Select(e => new TimelineEventDto
            {
                EventId = e.EventId,
                EventTime = e.EventTime,
                EventType = e.EventType,
                EventTypeName = e.EventTypeName ?? e.EventType.ToString(),
                EventValue = e.EventValue,
                DurationSeconds = e.DurationSeconds,
                ScrollDepth = e.ScrollDepth,
                PageUrl = e.PageUrl
            }).ToList());

        return new TaxpayerDetailDto
        {
            RecipientId = r.RecipientId,
            CampaignCode = r.Campaign.CampaignCode,
            CampaignName = r.Campaign.Name,
            MaskedNtn = r.MaskedNtn ?? "****",
            MobileMasked = r.MobileMasked,
            Language = r.Language,
            CreatedAt = r.CreatedAt,
            SmsSentAt = r.SmsSentAt,
            FirstVisitAt = r.FirstVisitAt,
            LastVisitAt = r.LastVisitAt,
            VisitCount = r.VisitCount,
            Sessions = sessions.Select(s => new TaxpayerSessionDto
            {
                SessionId = s.SessionId,
                StartedAt = s.StartedAt,
                EndedAt = s.EndedAt,
                LastHeartbeatAt = s.LastHeartbeatAt,
                DurationSeconds = s.DurationSeconds,
                MaxScrollDepth = s.MaxScrollDepth,
                VideoWatchSeconds = s.VideoWatchSeconds,
                VideoWatchPercent = s.VideoWatchPercent,
                RegisterClicked = s.RegisterClicked,
                FileClicked = s.FileClicked,
                TimeToFirstInteractionMs = s.TimeToFirstInteractionMs,
                IsBot = s.IsBot,
                IsBounce = s.IsBounce,
                IsEngaged = s.IsEngaged,
                Browser = s.Browser,
                OperatingSystem = s.OperatingSystem,
                DeviceType = s.DeviceType,
                IpAddress = s.IpAddress,
                Country = s.Country,
                City = s.City,
                ScreenResolution = s.ScreenResolution,
                Referrer = s.Referrer,
                Events = eventsBySession.TryGetValue(s.SessionId, out var ev) ? ev : new List<TimelineEventDto>()
            }).ToList()
        };
    }

    public async Task<int> GetActiveUsersAsync(long? campaignId, CancellationToken ct)
    {
        // "Active" = a heartbeat within TrackingLimits.ActiveUserWindowSeconds. The
        // tracking SDK fires every 10s, so this gives one missed heartbeat of slack.
        var threshold = DateTime.UtcNow.AddSeconds(-TrackingLimits.ActiveUserWindowSeconds);
        var q = db.Sessions.AsNoTracking().Where(s => s.LastHeartbeatAt >= threshold && !s.IsBot);
        if (campaignId.HasValue) q = q.Where(s => s.CampaignId == campaignId);
        return await q.CountAsync(ct);
    }

    public async Task<List<LiveEventDto>> GetRecentLiveEventsAsync(int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, TrackingLimits.MaxPageSize);
        var rows = await (from e in db.Events.AsNoTracking()
                          join s in db.Sessions.AsNoTracking() on e.SessionId equals s.SessionId
                          join r in db.Recipients.AsNoTracking() on s.RecipientId equals r.RecipientId
                          join c in db.Campaigns.AsNoTracking() on s.CampaignId equals c.CampaignId
                          where !s.IsBot
                          orderby e.EventTime descending
                          select new LiveEventDto
                          {
                              Kind = "event",
                              At = e.EventTime,
                              SessionId = e.SessionId,
                              CampaignId = e.CampaignId,
                              CampaignCode = c.CampaignCode,
                              RecipientId = e.RecipientId,
                              MaskedNtn = r.MaskedNtn ?? "****",
                              EventTypeName = e.EventTypeName ?? e.EventType.ToString(),
                              EventValue = e.EventValue,
                              Browser = s.Browser,
                              DeviceType = s.DeviceType.ToString(),
                              Country = s.Country,
                              City = s.City
                          }).Take(limit).ToListAsync(ct);
        return rows;
    }

    private static (DateTime From, DateTime To) ResolveRange(DashboardQuery q) =>
        (q.From?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30),
         q.To?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(1));
}
