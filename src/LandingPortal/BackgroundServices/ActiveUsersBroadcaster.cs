using Microsoft.EntityFrameworkCore;
using TaxpayerAnalytics.LandingPortal.Services.Dashboard;
using TaxpayerAnalytics.Shared.Entities;
using TaxpayerAnalytics.Shared.Enums;

namespace TaxpayerAnalytics.LandingPortal.BackgroundServices;

/// <summary>
/// Pushes the per-campaign live active-user count to the dashboard every 5 seconds.
/// Uses a scoped DbContext so the long-lived hosted service doesn't pin one open.
/// </summary>
public sealed class ActiveUsersBroadcaster(
    IServiceScopeFactory scopeFactory,
    IRealtimeNotifier realtime,
    ILogger<ActiveUsersBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var queries = scope.ServiceProvider.GetRequiredService<IDashboardQueryService>();

                var campaignIds = await db.Campaigns
                    .Where(c => c.Status == CampaignStatus.Active)
                    .Select(c => c.CampaignId)
                    .ToListAsync(stoppingToken);

                foreach (var id in campaignIds)
                {
                    var active = await queries.GetActiveUsersAsync(id, stoppingToken);
                    await realtime.PushActiveUsersAsync(id, active, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Active-users broadcast tick failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
