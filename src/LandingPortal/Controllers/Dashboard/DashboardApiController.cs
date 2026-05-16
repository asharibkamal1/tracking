using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.LandingPortal.Services.Dashboard;
using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Controllers.Dashboard;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardApiController(IDashboardQueryService queries) : ControllerBase
{
    [HttpGet("overview")]
    public Task<List<CampaignOverviewDto>> Overview([FromQuery] DashboardQuery q, CancellationToken ct) =>
        queries.GetAllCampaignsOverviewAsync(q, ct);

    [HttpGet("campaign/{id:long}")]
    public async Task<ActionResult<CampaignOverviewDto>> Campaign(long id, [FromQuery] DashboardQuery q, CancellationToken ct)
    {
        var c = await queries.GetCampaignOverviewAsync(id, q, ct);
        return c is null ? NotFound() : Ok(c);
    }

    [HttpGet("visits")]
    public Task<List<TimeSeriesPointDto>> Visits([FromQuery] DashboardQuery q, [FromQuery] string bucket = "hour", CancellationToken ct = default) =>
        queries.GetVisitsTimeSeriesAsync(q, bucket, ct);

    [HttpGet("devices")]
    public Task<List<DeviceBreakdownDto>> Devices([FromQuery] DashboardQuery q, CancellationToken ct) =>
        queries.GetDeviceBreakdownAsync(q, ct);

    [HttpGet("geo")]
    public Task<List<GeoBreakdownDto>> Geo([FromQuery] DashboardQuery q, [FromQuery] int top = 15, CancellationToken ct = default) =>
        queries.GetGeoBreakdownAsync(q, top, ct);

    [HttpGet("active-users")]
    public async Task<object> ActiveUsers([FromQuery] long? campaignId, CancellationToken ct) =>
        new { active = await queries.GetActiveUsersAsync(campaignId, ct), at = DateTime.UtcNow };

    [HttpGet("taxpayer/{recipientId:long}")]
    public async Task<ActionResult<TaxpayerDetailDto>> Taxpayer(long recipientId, CancellationToken ct)
    {
        var detail = await queries.GetTaxpayerDetailAsync(recipientId, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("taxpayers")]
    public async Task<object> Taxpayers(
        [FromQuery] DashboardQuery q,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var paged = await queries.GetTaxpayerListAsync(q, search, page, pageSize, ct);
        return paged;
    }

    [HttpGet("feed")]
    public Task<List<LiveEventDto>> Feed([FromQuery] int limit = 50, CancellationToken ct = default) =>
        queries.GetRecentLiveEventsAsync(limit, ct);
}
