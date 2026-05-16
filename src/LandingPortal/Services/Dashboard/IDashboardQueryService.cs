using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Services.Dashboard;

public interface IDashboardQueryService
{
    Task<List<CampaignOverviewDto>> GetAllCampaignsOverviewAsync(DashboardQuery q, CancellationToken ct);
    Task<CampaignOverviewDto?> GetCampaignOverviewAsync(long campaignId, DashboardQuery q, CancellationToken ct);
    Task<List<TimeSeriesPointDto>> GetVisitsTimeSeriesAsync(DashboardQuery q, string bucket, CancellationToken ct);
    Task<List<DeviceBreakdownDto>> GetDeviceBreakdownAsync(DashboardQuery q, CancellationToken ct);
    Task<List<GeoBreakdownDto>> GetGeoBreakdownAsync(DashboardQuery q, int top, CancellationToken ct);
    Task<PagedResult<TaxpayerRowDto>> GetTaxpayerListAsync(DashboardQuery q, string? search, int page, int pageSize, CancellationToken ct);
    Task<TaxpayerDetailDto?> GetTaxpayerDetailAsync(long recipientId, CancellationToken ct);
    Task<int> GetActiveUsersAsync(long? campaignId, CancellationToken ct);
    Task<List<LiveEventDto>> GetRecentLiveEventsAsync(int limit, CancellationToken ct);
}
