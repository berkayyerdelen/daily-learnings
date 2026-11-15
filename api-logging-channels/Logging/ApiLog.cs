using System.ComponentModel.DataAnnotations;

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
    /// <summary>
    /// false (default): only endpoints with [ApiLog] are logged.
    /// true: every controller endpoint is logged, except those with [SkipApiLog].
    /// </summary>
    public bool LogAllEndpoints { get; set; }

    /// <summary>Max logs held in memory. When full, new logs are dropped instead of slowing requests.</summary>
    [Range(1, 1_000_000)]
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    /// Max memory held by queued and in-flight logs. Bodies are not truncated, so a count limit
    /// alone does not bound memory (10,000 logs x 1 MB bodies = 10 GB). Whichever limit is hit
    /// first wins.
    /// </summary>
    [Range(typeof(long), "65536", "17179869184")] // 64 KB .. 16 GB
    public long MaxQueueBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// Writer loops inserting batches in parallel, each on its own connection. Default 1: all
    /// writers insert at the end of the same table, so on a plain table (no special index or
    /// lock settings) a second writer mostly waits for the first one's locks. Raise it only if
    /// a load test against your SQL Server shows a higher insert rate.
    /// </summary>
    [Range(1, 16)]
    public int WriterCount { get; set; } = 1;

    /// <summary>
    /// Insert when this many logs are collected... Capped at 4,000: SQL Server swaps a statement's
    /// row locks for a table lock at about 5,000 locks, which would block everything else on the
    /// table (other API instances, troubleshooting queries) until the batch commits. Staying under
    /// the threshold avoids that without changing any database setting.
    /// </summary>
    [Range(1, 4_000)]
    public int BatchSize { get; set; } = 1_000;

    /// <summary>...or when this much time has passed, whichever comes first.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:05:00")]
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Extra attempts for a batch that failed with a temporary error (timeout, lost connection,
    /// deadlock, failover). After that the batch is dropped and counted as failed.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Backoff before the first retry; doubles each attempt (with jitter), capped at 5 s.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:00:10")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>Counters for the logging pipeline, exposed at GET /api-logs/stats.</summary>
public sealed class ApiLogStats
{
    // No "enqueued" counter: it would be a second contended atomic write on every request.
    // Enqueued = inserted + failed + queued + in flight (see MapApiLogStats).
    private long _dropped, _inserted, _failed, _batches, _queuedBytes;
    private int _inFlight;

    /// <summary>Logs in batches currently being inserted (all writers).</summary>
    public int InFlight => Volatile.Read(ref _inFlight);
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

    public void AddInFlight(int delta) => Interlocked.Add(ref _inFlight, delta);
    public void OnDropped() => Interlocked.Increment(ref _dropped);

    public void OnBatchInserted(int count)
    {
        Interlocked.Add(ref _inserted, count);
        Interlocked.Increment(ref _batches);
    }

    public void OnBatchFailed(int count) => Interlocked.Add(ref _failed, count);
}

/// <summary>
/// Shared by <see cref="ApiLogAttribute"/> and <see cref="SkipApiLogAttribute"/>. Endpoint metadata
/// returns the most specific one (action over controller), so an action can override its controller.
/// </summary>
public interface IApiLogMetadata
{
    bool Enabled { get; }
}

/// <summary>
/// Logs this controller's actions, or this action, to the ApiLogs table.
/// Only endpoints with this attribute are logged, unless ApiLogging:LogAllEndpoints is true.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiLogAttribute : Attribute, IApiLogMetadata
{
    public bool Enabled => true;
}

/// <summary>
/// Excludes a controller or action from API logging, e.g. login inside a logged controller.
/// Wins over [ApiLog] on the controller, and over LogAllEndpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipApiLogAttribute : Attribute, IApiLogMetadata
{
    public bool Enabled => false;
}
