using Microsoft.EntityFrameworkCore;
using TaxpayerAnalytics.Shared.Entities;

namespace TaxpayerAnalytics.LandingPortal.Repositories;

public interface ISessionRepository
{
    Task<TaxpayerRecipient?> GetRecipientByTokenAsync(string token, CancellationToken ct);
    Task<UserSession> CreateSessionAsync(UserSession session, CancellationToken ct);
    Task IncrementVisitAsync(long recipientId, DateTime visitAt, CancellationToken ct);
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
}

public sealed class EventRepository(AnalyticsDbContext db) : IEventRepository
{
    public Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
        db.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    public async Task UpdateSessionMetricsAsync(Guid sessionId, Action<UserSession> mutate, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        if (session is null) return;
        mutate(session);
        await db.SaveChangesAsync(ct);
    }
}
