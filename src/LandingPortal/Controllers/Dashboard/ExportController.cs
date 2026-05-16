using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxpayerAnalytics.LandingPortal.Services.Dashboard;
using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Controllers.Dashboard;

[Authorize(Roles = "Admin")]
[Route("dashboard/export")]
public sealed class ExportController(
    IDashboardQueryService queries,
    IExcelExporter excel,
    IPdfExporter pdf) : Controller
{
    [HttpGet("excel")]
    public async Task<IActionResult> Excel([FromQuery] DashboardQuery q, CancellationToken ct)
    {
        var overviews = await queries.GetAllCampaignsOverviewAsync(q, ct);
        var paged = await queries.GetTaxpayerListAsync(q, search: null, page: 1, pageSize: 10_000, ct);
        var bytes = excel.BuildOverviewWorkbook(overviews, paged.Rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"analytics-{DateTime.UtcNow:yyyyMMddHHmm}.xlsx");
    }

    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf([FromQuery] DashboardQuery q, CancellationToken ct)
    {
        var overviews = await queries.GetAllCampaignsOverviewAsync(q, ct);
        var bytes = pdf.BuildOverviewReport(overviews);
        return File(bytes, "application/pdf", $"analytics-{DateTime.UtcNow:yyyyMMddHHmm}.pdf");
    }

    [HttpGet("taxpayer/{recipientId:long}/excel")]
    public async Task<IActionResult> TaxpayerExcel(long recipientId, CancellationToken ct)
    {
        var detail = await queries.GetTaxpayerDetailAsync(recipientId, ct);
        if (detail is null) return NotFound();
        var bytes = excel.BuildTaxpayerWorkbook(detail);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"taxpayer-{recipientId}-{DateTime.UtcNow:yyyyMMddHHmm}.xlsx");
    }

    [HttpGet("taxpayer/{recipientId:long}/pdf")]
    public async Task<IActionResult> TaxpayerPdf(long recipientId, CancellationToken ct)
    {
        var detail = await queries.GetTaxpayerDetailAsync(recipientId, ct);
        if (detail is null) return NotFound();
        var bytes = pdf.BuildTaxpayerReport(detail);
        return File(bytes, "application/pdf",
            $"taxpayer-{recipientId}-{DateTime.UtcNow:yyyyMMddHHmm}.pdf");
    }
}
