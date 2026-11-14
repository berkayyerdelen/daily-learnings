using System.ComponentModel.DataAnnotations;

namespace ApiLogging.Api.Logging;

public sealed class ApiLoggingOptions
{
    public const string SectionName = "ApiLogging";

    /// <summary>Which ADO.NET store to write to.</summary>
    public ApiLogProvider Provider { get; set; } = ApiLogProvider.SqlServer;

    /// <summary>Kill switch. When false the filter is a no-op.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of entries held in memory. This is the memory bound of the whole
    /// feature: when the database is slow or down, entries beyond this are dropped
    /// (and counted) instead of growing the heap or slowing requests down.
    /// </summary>
    [Range(1, 1_000_000)]
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Flush as soon as this many entries are collected.</summary>
    [Range(1, 50_000)]
    public int BatchSize { get; set; } = 500;

    /// <summary>Flush a partial batch after this long, so quiet periods still get written.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:05:00")]
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Number of consumer loops writing batches in parallel. Keep 1 unless one writer cannot
    /// keep up (watch api_logs.queue_depth); each extra writer holds one more DB connection.
    /// </summary>
    [Range(1, 32)]
    public int WriterConcurrency { get; set; } = 1;

    /// <summary>Retries per batch before it is dropped. 0 = no retry.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Fraction of successful requests whose bodies are logged (0..1). Metadata is always
    /// logged. At high volume, bodies dominate both request-thread CPU (serialization) and
    /// storage, so sampling them is the biggest single lever.
    /// </summary>
    [Range(0.0, 1.0)]
    public double BodySampleRate { get; set; } = 1.0;

    /// <summary>Log bodies for every 4xx/5xx, regardless of <see cref="BodySampleRate"/>.</summary>
    public bool AlwaysLogBodiesOnError { get; set; } = true;

    /// <summary>Request/response bodies and exception text are cut to this many characters.</summary>
    [Range(0, 1_000_000)]
    public int MaxBodyLength { get; set; } = 8_192;

    /// <summary>Timeout for one batch write.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Create the table at startup. Convenient for a demo; in production the schema
    /// belongs to a migration step, not to every replica on boot.
    /// </summary>
    public bool EnsureSchema { get; set; }
}

public enum ApiLogProvider
{
    SqlServer,
    Postgres
}
