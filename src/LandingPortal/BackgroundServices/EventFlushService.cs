using TaxpayerAnalytics.LandingPortal.Services;
using TaxpayerAnalytics.Shared.Entities;

namespace TaxpayerAnalytics.LandingPortal.BackgroundServices;

/// <summary>
/// Drains the in-memory queue into SQL Server in batches. A fresh DbContext per flush
/// keeps the change-tracker bounded; a 2s ceiling ensures partial batches still land
/// under light load.
/// </summary>
public sealed class EventFlushService(
    IEventIngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<EventFlushService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan MaxBatchAge = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<EventLog>(BatchSize);
        var batchStartedAt = DateTime.UtcNow;

        await foreach (var evt in queue.ReadAllAsync(stoppingToken))
        {
            buffer.Add(evt);
            if (buffer.Count >= BatchSize || DateTime.UtcNow - batchStartedAt >= MaxBatchAge)
            {
                await FlushAsync(buffer, stoppingToken);
                buffer.Clear();
                batchStartedAt = DateTime.UtcNow;
            }
        }

        if (buffer.Count > 0) await FlushAsync(buffer, CancellationToken.None);
    }

    private async Task FlushAsync(List<EventLog> batch, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            await db.Events.AddRangeAsync(batch, ct);
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Flushed {Count} events to SQL", batch.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush {Count} events", batch.Count);
        }
    }
}
