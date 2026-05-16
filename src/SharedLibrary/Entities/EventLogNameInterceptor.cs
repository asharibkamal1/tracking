using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaxpayerAnalytics.Shared.Entities;

/// <summary>
/// Auto-populates <see cref="EventLog.EventTypeName"/> from the enum value at save
/// time. Lets every caller (controllers, background flush, future ingest paths)
/// stay ignorant of the column — the string and the int stay in sync by
/// construction.
/// </summary>
public sealed class EventLogNameInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampNames(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        StampNames(eventData);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static void StampNames(DbContextEventData eventData)
    {
        if (eventData.Context is null) return;
        foreach (var entry in eventData.Context.ChangeTracker.Entries<EventLog>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            entry.Entity.EventTypeName = entry.Entity.EventType.ToString();
        }
    }
}
