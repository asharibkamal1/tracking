using System.Threading.Channels;
using TaxpayerAnalytics.Shared.Entities;

namespace TaxpayerAnalytics.LandingPortal.Services;

public interface IEventIngestionQueue
{
    bool TryEnqueue(EventLog evt);
    IAsyncEnumerable<EventLog> ReadAllAsync(CancellationToken ct);
}

/// <summary>
/// Bounded in-memory queue between API controllers and the EventFlushService.
/// Drops oldest on overflow rather than blocking the request — losing a non-critical
/// analytic event is preferable to back-pressuring into the user's browser.
/// </summary>
public sealed class EventIngestionQueue : IEventIngestionQueue
{
    private readonly Channel<EventLog> _channel =
        Channel.CreateBounded<EventLog>(new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

    public bool TryEnqueue(EventLog evt) => _channel.Writer.TryWrite(evt);

    public async IAsyncEnumerable<EventLog> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct))
            while (_channel.Reader.TryRead(out var item))
                yield return item;
    }
}
