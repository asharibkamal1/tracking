using Microsoft.EntityFrameworkCore;
using TaxpayerAnalytics.Shared.Entities;

namespace TaxpayerAnalytics.LandingPortal.Repositories;

public interface ISessionRepository
{
    Task<TaxpayerRecipient?> GetRecipientByTokenAsync(string token, CancellationToken ct);
    Task<UserSession?> GetRecentSessionForRecipientAsync(long recipientId, DateTime since, CancellationToken ct);
    Task<UserSession> CreateSessionAsync(UserSession session, CancellationToken ct);
    Task IncrementVisitAsync(long recipientId, DateTime visitAt, CancellationToken ct);
    Task TouchSessionAsync(Guid sessionId, DateTime at, CancellationToken ct);
}

public interface IEventRepository
{
    Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken ct);
    Task UpdateSessionMetricsAsync(Guid sessionId, Action<UserSession> mutate, CancellationToken ct);
}

public sealed class SessionRepository(AnalyticsDbContext db) : ISessionRepository
{
    public Task<TaxpayerRecipient?> GetRecipientByTokenAsync(string token, CancellationToken ct) =>
        db.Recipients
            .Include(r => r.Campaign)
            .FirstOrDefaultAsync(r => r.TrackingToken == token, ct);

    public Task<UserSession?> GetRecentSessionForRecipientAsync(long recipientId, DateTime since, CancellationToken ct) =>
        db.Sessions
            .Where(s => s.RecipientId == recipientId
                     && s.LastHeartbeatAt >= since
                     && !s.IsBot)
            .OrderByDescending(s => s.LastHeartbeatAt)
            .FirstOrDefaultAsync(ct);

    public async Task<UserSession> CreateSessionAsync(UserSession session, CancellationToken ct)
    {
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task IncrementVisitAsync(long recipientId, DateTime visitAt, CancellationToken ct)
    {
        await db.Recipients
            .Where(r => r.RecipientId == recipientId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.VisitCount, r => r.VisitCount + 1)
                .SetProperty(r => r.LastVisitAt, visitAt)
                .SetProperty(r => r.FirstVisitAt, r => r.FirstVisitAt ?? visitAt), ct);
    }

    public async Task TouchSessionAsync(Guid sessionId, DateTime at, CancellationToken ct)
    {
        await db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastHeartbeatAt, at), ct);
    }
}

public sealed class EventRepository(AnalyticsDbContext db, ILogger<EventRepository> logger) : IEventRepository
{
    public Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
        db.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    public async Task UpdateSessionMetricsAsync(Guid sessionId, Action<UserSession> mutate, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        if (session is null)
        {
            // Possible race: events arrive after the session row was deleted (test reset)
            // or a client sent a forged SessionId. Skip silently after a warning.
            logger.LogWarning("UpdateSessionMetrics: session {Sid} not found", sessionId);
            return;
        }
        mutate(session);
        await db.SaveChangesAsync(ct);
    }
}
