namespace ApiLogging.Api.Logging;

/// <summary>
/// One captured request/response pair. Immutable on purpose: it is created on the
/// request thread and read later by the background writer, so nothing may mutate it
/// after it has been put on the channel.
/// </summary>
public sealed record ApiLogEntry
{
    public required DateTimeOffset OccurredAt { get; init; }
    public required string TraceId { get; init; }
    public required string HttpMethod { get; init; }
    public required string Path { get; init; }
    public string? QueryString { get; init; }
    public string? Endpoint { get; init; }
    public required int StatusCode { get; init; }
    public required double DurationMs { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
    public string? ClientIp { get; init; }
    public string? UserAgent { get; init; }
    public string? UserId { get; init; }
    public string? Exception { get; init; }
}
