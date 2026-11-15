using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Reads logs from the channel with WriterCount parallel writers and inserts them in batches: when
/// BatchSize logs are collected, or when FlushInterval has passed, whichever comes first.
///
/// Failure handling, by design goal:
///  * Logging must never take the API down. Each writer runs under a supervisor: an unexpected
///    exception (a bug, not a database error) is logged and the writer restarts. Without this, .NET's
///    default for a faulted BackgroundService is to stop the whole host.
///  * Database blips are absorbed: a batch that fails with a temporary error is retried with
///    exponential backoff and jitter.
///  * A database that is down is not hammered: after a batch is given up on, writers wait before
///    the next attempt (circuit-breaker style cooldown, growing up to MaxFailureCooldown, reset on the
///    first success). Meanwhile the queue fills and the filter drops new logs (with a warning).
///  * Shutdown is bounded: on stop, writers drain the queue without retries, and inserts are cancelled
///    at a deadline just inside the host's ShutdownTimeout, so a dead database can't hang a deploy.
///  * The memory budget is always released, whatever happens to a batch.
/// </summary>
public sealed class ApiLogBackgroundService : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WriterRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShutdownMargin = TimeSpan.FromSeconds(2);

    private readonly ChannelReader<ApiLog> _reader;
    private readonly IApiLogRepository _repository;
    private readonly ApiLogMemoryBudget _budget;
    private readonly ApiLogOptions _options;
    private readonly ILogger<ApiLogBackgroundService> _logger;

    // Cancelled once the drain after stop has used up its time. Separate from stoppingToken, which
    // fires at the *start* of shutdown, when we still want to write what is queued.
    private readonly CancellationTokenSource _drainDeadline = new();
    private readonly TimeSpan _drainTimeout;
    private CancellationTokenRegistration _onStopping;

    // Batches given up on in a row, across all writers. Drives the cooldown; 0 = healthy.
    private int _consecutiveFailures;

    public ApiLogBackgroundService(
        ChannelReader<ApiLog> reader,
        IApiLogRepository repository,
        ApiLogMemoryBudget budget,
        IOptions<ApiLogOptions> options,
        IOptions<HostOptions> hostOptions,
        ILogger<ApiLogBackgroundService> logger)
    {
        _reader = reader;
        _repository = repository;
        _budget = budget;
        _options = options.Value;
        _logger = logger;

        // Finish before the host gives up on us, or the remaining logs are lost mid-insert anyway.
        var shutdownTimeout = hostOptions.Value.ShutdownTimeout;
        _drainTimeout = shutdownTimeout > ShutdownMargin + TimeSpan.FromSeconds(1)
            ? shutdownTimeout - ShutdownMargin
            : TimeSpan.FromSeconds(1);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _onStopping = stoppingToken.Register(() => _drainDeadline.CancelAfter(_drainTimeout));

        // WriterCount independent loops reading the same channel. Each owns its batch list, and the
        // repository is stateless, so they share nothing but the channel (which is built for this).
        return Task.WhenAll(Enumerable.Range(1, Math.Max(1, _options.WriterCount))
            .Select(writer => SuperviseAsync(writer, stoppingToken)));
    }

    public override void Dispose()
    {
        _onStopping.Dispose();
        _drainDeadline.Dispose();
        base.Dispose();
    }

    /// <summary>Runs one writer and restarts it if it crashes. Never throws.</summary>
    private async Task SuperviseAsync(int writer, CancellationToken stoppingToken)
    {
        // BackgroundService calls ExecuteAsync synchronously; yield so writers never delay startup.
        await Task.Yield();

        while (!_drainDeadline.IsCancellationRequested)
        {
            try
            {
                await RunWriterAsync(stoppingToken);
                return; // finished draining after stop
            }
            catch (Exception ex)
            {
                // Database errors are handled in FlushAsync, so this is a bug. Restart instead of
                // letting the exception reach the host, which would stop the whole API.
                _logger.LogCritical(ex, "API log writer {Writer} crashed; restarting in {Delay} s",
                    writer, WriterRestartDelay.TotalSeconds);
                try { await Task.Delay(WriterRestartDelay, _drainDeadline.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task RunWriterAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiLog>(_options.BatchSize);
        try
        {
            try
            {
                // Wait (without polling) until at least one log arrives.
                while (await _reader.WaitToReadAsync(stoppingToken))
                {
                    await FillBatchAsync(batch, stoppingToken);
                    await FlushAsync(batch, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            // Shutdown: drain what is left (all writers in parallel), within the deadline.
            while (!_drainDeadline.IsCancellationRequested && _reader.TryRead(out var log))
            {
                batch.Add(log);
                if (batch.Count >= _options.BatchSize) await FlushAsync(batch, stoppingToken);
            }
            await FlushAsync(batch, stoppingToken);

            if (_drainDeadline.IsCancellationRequested && _reader.Count > 0)
                _logger.LogWarning("Shutdown timeout reached; {Count} API logs were not written", _reader.Count);
        }
        finally
        {
            // Only non-empty if this writer crashed mid-batch: release the memory those logs reserved,
            // or the budget would leak until the filter drops everything.
            if (batch.Count > 0)
            {
                _logger.LogError("Discarding {Count} API logs held by a crashed writer", batch.Count);
                ReleaseAndClear(batch);
            }
        }
    }

    private async Task FillBatchAsync(List<ApiLog> batch, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(_options.FlushInterval);

        try
        {
            while (batch.Count < _options.BatchSize)
            {
                while (batch.Count < _options.BatchSize && _reader.TryRead(out var log))
                    batch.Add(log);

                if (batch.Count < _options.BatchSize)
                    await _reader.WaitToReadAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Flush interval elapsed (or shutting down): insert what we have.
        }
    }

    private async Task FlushAsync(List<ApiLog> batch, CancellationToken stoppingToken)
    {
        if (batch.Count == 0) return;

        try
        {
            await WaitForCooldownAsync(stoppingToken);

            for (var attempt = 1; ; attempt++)
            {
                // The drain deadline only fires once shutdown has used up its time, so while running
                // this behaves like no token (the repository's own timeouts apply). It must be passed
                // to every insert, not just those started after stop: an insert already in flight
                // when a deploy begins would otherwise hang shutdown on a dead database.
                var stopping = stoppingToken.IsCancellationRequested;
                try
                {
                    await _repository.InsertBatchAsync(batch, _drainDeadline.Token);
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    return;
                }
                catch (Exception ex) when (!stopping && attempt <= _options.MaxRetries && IsTransient(ex))
                {
                    var delay = RetryDelay(attempt);
                    _logger.LogWarning(ex,
                        "Inserting {Count} API logs failed with a temporary error (attempt {Attempt} of {Total}); retrying in {Delay} ms",
                        batch.Count, attempt, _options.MaxRetries + 1, (int)delay.TotalMilliseconds);

                    // If shutdown starts during the wait, stop waiting and make one last attempt
                    // (no more retries) under the drain deadline.
                    try { await Task.Delay(delay, stoppingToken); }
                    catch (OperationCanceledException) { }
                }
                catch (Exception ex)
                {
                    var failures = Interlocked.Increment(ref _consecutiveFailures);
                    _logger.LogError(ex,
                        "Dropping {Count} API logs after {Attempts} attempt(s) ({Failures} failed batch(es) in a row)",
                        batch.Count, attempt, failures);
                    return;
                }
            }
        }
        finally
        {
            ReleaseAndClear(batch);
        }
    }

    /// <summary>
    /// Circuit-breaker style pause after failed batches: base * 2^failures, capped at
    /// MaxFailureCooldown, with jitter so instances don't retry in lockstep. Skipped during shutdown,
    /// where each batch gets one attempt within the drain deadline instead.
    /// </summary>
    private async Task WaitForCooldownAsync(CancellationToken stoppingToken)
    {
        var failures = Volatile.Read(ref _consecutiveFailures);
        if (failures == 0 || stoppingToken.IsCancellationRequested) return;

        var ceiling = Math.Min(_options.MaxFailureCooldown.TotalMilliseconds,
            _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failures, 30)));
        var cooldown = TimeSpan.FromMilliseconds(ceiling / 2 + Random.Shared.NextDouble() * ceiling / 2);

        try { await Task.Delay(cooldown, stoppingToken); }
        catch (OperationCanceledException) { } // shutting down: go straight to the final attempt
    }

    private bool IsTransient(Exception exception)
    {
        try
        {
            return _repository.IsTransient(exception);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IsTransient threw; treating the error as permanent");
            return false;
        }
    }

    private void ReleaseAndClear(List<ApiLog> batch)
    {
        long bytes = 0;
        foreach (var log in batch) bytes += log.ApproximateBytes;
        _budget.Release(bytes);
        batch.Clear(); // keeps the list's capacity: no new allocation per batch
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
