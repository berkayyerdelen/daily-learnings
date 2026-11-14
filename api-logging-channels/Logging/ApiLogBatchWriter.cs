using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Consumer of <see cref="ApiLogQueue"/> (one loop by default, see
/// <see cref="ApiLoggingOptions.WriterConcurrency"/>). Collects entries into batches and writes
/// each batch with one database round trip.
///
/// A batch is flushed when either
///   * it reaches <see cref="ApiLoggingOptions.BatchSize"/> (throughput under load), or
///   * <see cref="ApiLoggingOptions.FlushInterval"/> has passed since its first entry
///     (latency when traffic is low).
///
/// Why <see cref="IHostedLifecycleService"/> and not BackgroundService:
/// hosted services stop in reverse registration order, and the web server was registered
/// before us, so our StopAsync runs while Kestrel is still finishing in-flight requests.
/// If we closed the queue there, the last requests' logs would be dropped. Instead we
/// close the queue in <see cref="StoppedAsync"/>, which runs after every StopAsync
/// (Kestrel included), then drain what is left within the host's shutdown timeout.
/// </summary>
public sealed class ApiLogBatchWriter : IHostedLifecycleService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);

    private readonly ApiLogQueue _queue;
    private readonly IApiLogStore _store;
    private readonly ApiLoggingMetrics _metrics;
    private readonly ApiLoggingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ApiLogBatchWriter> _logger;
    private Task? _loops;

    public ApiLogBatchWriter(
        ApiLogQueue queue,
        IApiLogStore store,
        ApiLoggingMetrics metrics,
        IOptions<ApiLoggingOptions> options,
        TimeProvider time,
        ILogger<ApiLogBatchWriter> logger)
    {
        _queue = queue;
        _store = store;
        _metrics = metrics;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Task.Run so the loops never run on (or block) the host's startup thread.
        _loops = Task.WhenAll(Enumerable.Range(0, _options.WriterConcurrency)
            .Select(_ => Task.Run(RunAsync, CancellationToken.None)));
        return Task.CompletedTask;
    }

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        if (_loops is null) return;

        try
        {
            // cancellationToken fires when the host's ShutdownTimeout expires.
            await _loops.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Shutdown timeout reached; {Remaining} API log entries were not written", _queue.Count);
        }
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync()
    {
        var reader = _queue.Reader;
        var batch = new List<ApiLogEntry>(_options.BatchSize);

        try
        {
            // Returns false only once the queue is completed AND empty, so this loop
            // naturally drains everything on shutdown. It deliberately takes no token.
            while (await reader.WaitToReadAsync())
            {
                await FillBatchAsync(reader, batch);
                await FlushAsync(batch);
                batch.Clear();
            }
        }
        catch (Exception ex)
        {
            // FlushAsync swallows store errors, so reaching this is a bug. Make it loud.
            _logger.LogCritical(ex, "API log writer loop crashed; API logs will no longer be persisted");
        }
    }

    private async Task FillBatchAsync(ChannelReader<ApiLogEntry> reader, List<ApiLogEntry> batch)
    {
        // One timer per batch (not per entry). The window starts at the first entry.
        using var window = new CancellationTokenSource(_options.FlushInterval, _time);

        while (batch.Count < _options.BatchSize)
        {
            if (reader.TryRead(out var entry))
            {
                batch.Add(entry);
                continue;
            }

            try
            {
                if (!await reader.WaitToReadAsync(window.Token))
                    return; // completed: flush what we have
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested)
            {
                return; // flush interval elapsed
            }
        }
    }

    private async Task FlushAsync(List<ApiLogEntry> batch)
    {
        if (batch.Count == 0) return;

        for (var attempt = 1; ; attempt++)
        {
            var started = _time.GetTimestamp();
            try
            {
                using var timeout = new CancellationTokenSource(_options.WriteTimeout, _time);
                await _store.WriteBatchAsync(batch, timeout.Token);
                _metrics.Written(batch.Count, _time.GetElapsedTime(started));
                return;
            }
            catch (Exception ex) when (attempt <= _options.MaxRetries)
            {
                var delay = Backoff(attempt);
                _logger.LogWarning(ex,
                    "Writing {Count} API log entries failed (attempt {Attempt}/{Total}); retrying in {Delay}",
                    batch.Count, attempt, _options.MaxRetries + 1, delay);
                await Task.Delay(delay, _time);
            }
            catch (Exception ex)
            {
                // Logs are not worth blocking the pipeline for. Drop this batch and move on
                // so newer entries still get a chance; the metric makes the loss visible.
                _metrics.Failed(batch.Count);
                _logger.LogError(ex, "Dropping {Count} API log entries after {Attempts} failed attempts",
                    batch.Count, attempt);
                return;
            }
        }
    }

    /// <summary>Exponential backoff with full jitter, so replicas do not retry in lockstep.</summary>
    private static TimeSpan Backoff(int attempt)
    {
        var ceiling = Math.Min(MaxBackoff.TotalMilliseconds, 100 * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
    }
}
