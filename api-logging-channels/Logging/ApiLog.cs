using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

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

/// <summary>
/// Caps the memory held by queued and in-flight logs (bodies are not truncated, so a count limit
/// alone does not bound memory). Memory is reserved when a log is queued and released only after
/// its batch is written or given up on, so the budget also covers batches being inserted.
/// </summary>
public sealed class ApiLogMemoryBudget(IOptions<ApiLogOptions> options)
{
    private readonly long _maxBytes = options.Value.MaxQueueBytes;
    private long _usedBytes;

    public long UsedBytes => Interlocked.Read(ref _usedBytes);

    /// <summary>Reserves memory for a log; false (nothing reserved) if it would exceed the budget.</summary>
    public bool TryReserve(long bytes)
    {
        if (Interlocked.Add(ref _usedBytes, bytes) <= _maxBytes) return true;
        Interlocked.Add(ref _usedBytes, -bytes);
        return false;
    }

    public void Release(long bytes) => Interlocked.Add(ref _usedBytes, -bytes);
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
