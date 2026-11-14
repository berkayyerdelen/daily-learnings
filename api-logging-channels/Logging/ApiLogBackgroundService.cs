using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Reads logs from the channel with WriterCount parallel writers and inserts them in batches: when BatchSize logs are
/// collected, or when FlushInterval has passed, whichever comes first. On shutdown it
/// writes whatever is left in the channel.
///
/// A batch that fails with a temporary error is retried with exponential backoff and jitter;
/// other errors, or running out of retries, drop the batch so newer logs are not held up.
/// </summary>
public sealed class ApiLogBackgroundService(
    ChannelReader<ApiLog> reader,
    IApiLogRepository repository,
    ApiLogStats stats,
    IOptions<ApiLogOptions> options,
    ILogger<ApiLogBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly ApiLogOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // WriterCount independent loops reading the same channel. Each owns its batch list, and the
        // repository is stateless, so they share nothing but the channel (which is built for this).
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, _options.WriterCount))
            .Select(_ => RunWriterAsync(stoppingToken)));

    private async Task RunWriterAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiLog>(_options.BatchSize);

        try
        {
            // Wait (without polling) until at least one log arrives.
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                await FillBatchAsync(batch, stoppingToken);
                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Shutdown: drain what is left (all writers drain in parallel).
        while (reader.TryRead(out var log))
        {
            batch.Add(log);
            if (batch.Count >= _options.BatchSize) await FlushAsync(batch);
        }
        await FlushAsync(batch);
    }

    private async Task FillBatchAsync(List<ApiLog> batch, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(_options.FlushInterval);

        try
        {
            while (batch.Count < _options.BatchSize)
            {
                while (batch.Count < _options.BatchSize && reader.TryRead(out var log))
                    batch.Add(log);

                if (batch.Count < _options.BatchSize)
                    await reader.WaitToReadAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Flush interval elapsed (or shutting down): insert what we have.
        }
    }

    private async Task FlushAsync(List<ApiLog> batch)
    {
        if (batch.Count == 0) return;

        stats.AddInFlight(batch.Count);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // Not tied to stoppingToken, so a batch in progress at shutdown still completes.
                    await repository.InsertBatchAsync(batch, CancellationToken.None);
                    stats.OnBatchInserted(batch.Count);
                    return;
                }
                catch (Exception ex) when (attempt <= _options.MaxRetries && repository.IsTransient(ex))
                {
                    var delay = RetryDelay(attempt);
                    logger.LogWarning(ex,
                        "Inserting {Count} API logs failed with a temporary error (attempt {Attempt} of {Total}); retrying in {Delay} ms",
                        batch.Count, attempt, _options.MaxRetries + 1, (int)delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    stats.OnBatchFailed(batch.Count);
                    logger.LogError(ex, "Dropping {Count} API logs after {Attempts} attempt(s)", batch.Count, attempt);
                    return;
                }
            }
        }
        finally
        {
            long bytes = 0;
            foreach (var log in batch) bytes += log.ApproximateBytes;
            stats.Release(bytes);
            stats.AddInFlight(-batch.Count);
            batch.Clear(); // keeps the list's capacity: no new allocation per batch
        }
    }

    /// <summary>
    /// Exponential backoff with full jitter: random delay in [0, base * 2^(attempt-1)], capped.
    /// The randomness stops every API instance from retrying at the same moment after a failover.
    /// </summary>
    private TimeSpan RetryDelay(int attempt)
    {
        var ceiling = Math.Min(MaxRetryDelay.TotalMilliseconds,
            _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
    }
}
