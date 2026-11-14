namespace ApiLogging.Api.Logging;

public sealed record ApiLog(
    DateTime CreatedAt,
    string TraceId,
    string Method,
    string Path,
    string? QueryString,
    int StatusCode,
    long DurationMs,
    string? RequestBody,
    string? ResponseBody,
    string? Exception)
{
    /// <summary>
    /// Approximate memory held by this log (.NET strings are UTF-16: 2 bytes per char, plus
    /// ~150 bytes of object overhead). Computed once and used to enforce the memory budget.
    /// </summary>
    public long ApproximateBytes { get; } = 150 + 2L * (
        TraceId.Length + Method.Length + Path.Length + (QueryString?.Length ?? 0) +
        (RequestBody?.Length ?? 0) + (ResponseBody?.Length ?? 0) + (Exception?.Length ?? 0));
}

public sealed class ApiLogOptions
{
    /// <summary>Max logs held in memory. When full, new logs are dropped instead of slowing requests.</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    /// Max memory held by queued and in-flight logs. Bodies are not truncated, so a count limit
    /// alone does not bound memory (10,000 logs x 1 MB bodies = 10 GB). Whichever limit is hit
    /// first wins.
    /// </summary>
    public long MaxQueueBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Insert when this many logs are collected...</summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>...or when this much time has passed, whichever comes first.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Extra attempts for a batch that failed with a temporary error (timeout, lost connection,
    /// deadlock, failover). After that the batch is dropped and counted as failed.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Backoff before the first retry; doubles each attempt (with jitter), capped at 5 s.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>Counters for the logging pipeline, exposed at GET /api-logs/stats.</summary>
public sealed class ApiLogStats
{
    private long _enqueued, _dropped, _inserted, _failed, _batches, _queuedBytes;

    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Inserted => Interlocked.Read(ref _inserted);
    public long Failed => Interlocked.Read(ref _failed);
    public long Batches => Interlocked.Read(ref _batches);
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    /// <summary>Reserves memory for a log; false (nothing reserved) if it would exceed the budget.</summary>
    public bool TryReserve(long bytes, long maxBytes)
    {
        if (Interlocked.Add(ref _queuedBytes, bytes) <= maxBytes) return true;
        Interlocked.Add(ref _queuedBytes, -bytes);
        return false;
    }

    /// <summary>Releases memory once logs are written (or given up on), not when dequeued,
    /// so the budget also covers the batch being inserted.</summary>
    public void Release(long bytes) => Interlocked.Add(ref _queuedBytes, -bytes);

    public void OnEnqueued() => Interlocked.Increment(ref _enqueued);
    public void OnDropped() => Interlocked.Increment(ref _dropped);

    public void OnBatchInserted(int count)
    {
        Interlocked.Add(ref _inserted, count);
        Interlocked.Increment(ref _batches);
    }

    public void OnBatchFailed(int count) => Interlocked.Add(ref _failed, count);
}

/// <summary>Excludes a controller or action from API logging (e.g. login).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipApiLogAttribute : Attribute;
