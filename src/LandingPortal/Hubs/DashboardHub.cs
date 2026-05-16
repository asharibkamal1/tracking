using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TaxpayerAnalytics.LandingPortal.Hubs;

[Authorize(Roles = "Admin")]
public sealed class DashboardHub : Hub
{
    /// <summary>Subscribes the caller to the all-campaigns firehose.</summary>
    public Task JoinAll() => Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.All);
    public Task LeaveAll() => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNames.All);

    /// <summary>Subscribes the caller to events for a specific campaign.</summary>
    public Task JoinCampaign(long campaignId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Campaign(campaignId));
    public Task LeaveCampaign(long campaignId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNames.Campaign(campaignId));

    /// <summary>Subscribes the caller to events for a specific recipient (timeline view).</summary>
    public Task JoinRecipient(long recipientId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Recipient(recipientId));
    public Task LeaveRecipient(long recipientId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNames.Recipient(recipientId));

    public static class GroupNames
    {
        public const string All = "dashboard:all";
        public static string Campaign(long id) => $"dashboard:campaign:{id}";
        public static string Recipient(long id) => $"dashboard:recipient:{id}";
    }
}
