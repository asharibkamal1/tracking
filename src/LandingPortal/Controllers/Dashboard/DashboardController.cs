using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.LandingPortal.Services.Dashboard;
using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Controllers.Dashboard;

[Authorize(Roles = "Admin")]
[Route("dashboard")]
public sealed class DashboardController(IDashboardQueryService queries) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] DashboardQuery q, CancellationToken ct)
    {
        var rows = await queries.GetAllCampaignsOverviewAsync(q, ct);
        ViewBag.Query = q;
        return View(rows);
    }

    [HttpGet("campaign/{id:long}")]
    public async Task<IActionResult> Campaign(long id, [FromQuery] DashboardQuery q, CancellationToken ct)
    {
        q.CampaignId = id;
        var overview = await queries.GetCampaignOverviewAsync(id, q, ct);
        if (overview is null) return NotFound();
        ViewBag.Query = q;
        return View(overview);
    }

    [HttpGet("taxpayers")]
    public async Task<IActionResult> Taxpayers(
        [FromQuery] DashboardQuery q,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var paged = await queries.GetTaxpayerListAsync(q, search, page, pageSize, ct);
        ViewBag.Query = q;
        ViewBag.Search = search;
        return View(paged);
    }

    [HttpGet("taxpayers/{recipientId:long}")]
    public async Task<IActionResult> Taxpayer(long recipientId, CancellationToken ct)
    {
        var detail = await queries.GetTaxpayerDetailAsync(recipientId, ct);
        if (detail is null) return NotFound();
        return View(detail);
    }

    [HttpGet("feed")]
    public async Task<IActionResult> Feed(CancellationToken ct)
    {
        var recent = await queries.GetRecentLiveEventsAsync(50, ct);
        return View(recent);
    }
}
