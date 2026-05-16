using Microsoft.AspNetCore.SignalR;
using TaxpayerAnalytics.LandingPortal.Hubs;
using TaxpayerAnalytics.Shared.Dtos;

namespace TaxpayerAnalytics.LandingPortal.Services.Dashboard;

public interface IRealtimeNotifier
{
    Task PushEventAsync(LiveEventDto evt, CancellationToken ct = default);
    Task PushActiveUsersAsync(long campaignId, int active, CancellationToken ct = default);
}

/// <summary>
/// Thin wrapper over IHubContext so controllers don't need to know about SignalR group
/// names. Always fans out to the "all" firehose plus the per-campaign and
/// per-recipient groups so any dashboard view can stay live.
/// </summary>
public sealed class RealtimeNotifier(IHubContext<DashboardHub> hub) : IRealtimeNotifier
{
    public async Task PushEventAsync(LiveEventDto evt, CancellationToken ct = default)
    {
        await Task.WhenAll(
            hub.Clients.Group(DashboardHub.GroupNames.All).SendAsync("liveEvent", evt, ct),
            hub.Clients.Group(DashboardHub.GroupNames.Campaign(evt.CampaignId)).SendAsync("liveEvent", evt, ct),
            hub.Clients.Group(DashboardHub.GroupNames.Recipient(evt.RecipientId)).SendAsync("liveEvent", evt, ct));
    }

    public Task PushActiveUsersAsync(long campaignId, int active, CancellationToken ct = default)
    {
        var payload = new { campaignId, active, at = DateTime.UtcNow };
        return Task.WhenAll(
            hub.Clients.Group(DashboardHub.GroupNames.All).SendAsync("activeUsers", payload, ct),
            hub.Clients.Group(DashboardHub.GroupNames.Campaign(campaignId)).SendAsync("activeUsers", payload, ct));
    }
}
