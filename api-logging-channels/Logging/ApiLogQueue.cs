using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// The hand-off point between request threads (many writers) and the background
/// writer (one reader).
///
/// Design choices:
///  * Bounded, so memory use has a ceiling no matter how long the database is down.
///  * Writers only ever call <see cref="ChannelWriter{T}.TryWrite"/>, never WriteAsync.
///    A full queue must never add latency to an API call; we drop and count instead.
///  * FullMode = Wait, not DropWrite. With Wait, TryWrite returns false when full, so we
///    know about every drop. With DropWrite, TryWrite returns true and the item is
///    silently discarded.
/// </summary>
public sealed class ApiLogQueue
{
    private readonly Channel<ApiLogEntry> _channel;
    private readonly ApiLoggingMetrics _metrics;

    public ApiLogQueue(IOptions<ApiLoggingOptions> options, ApiLoggingMetrics metrics)
    {
        _metrics = metrics;
        _channel = Channel.CreateBounded<ApiLogEntry>(new BoundedChannelOptions(options.Value.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            // SingleReader enables a faster channel implementation when there is one writer loop.
            SingleReader = options.Value.WriterConcurrency == 1,
            SingleWriter = false,
            // Do not let the reader's continuation run inline on a request thread.
            AllowSynchronousContinuations = false
        });

        metrics.RegisterQueueDepth(() => _channel.Reader.Count);
    }

    internal ChannelReader<ApiLogEntry> Reader => _channel.Reader;

    public int Count => _channel.Reader.Count;

    /// <summary>Non-blocking. Returns false (and counts a drop) if the queue is full or closed.</summary>
    public bool TryEnqueue(ApiLogEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
        {
            _metrics.Enqueued();
            return true;
        }

        _metrics.Dropped();
        return false;
    }

    /// <summary>Stop accepting entries. The reader drains what is left and then finishes.</summary>
    internal void Complete() => _channel.Writer.TryComplete();
}
