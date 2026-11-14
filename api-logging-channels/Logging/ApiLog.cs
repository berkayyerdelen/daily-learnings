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
    string? Exception);

public sealed class ApiLogOptions
{
    /// <summary>Max logs held in memory. When full, new logs are dropped instead of slowing requests.</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>Insert when this many logs are collected...</summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>...or when this much time has passed, whichever comes first.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Bodies longer than this are truncated.</summary>
    public int MaxBodyLength { get; set; } = 4_000;
}

/// <summary>Excludes a controller or action from API logging (e.g. login).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipApiLogAttribute : Attribute;
