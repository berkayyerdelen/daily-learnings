using System.Diagnostics.Metrics;

namespace ApiLogging.Api.Logging;

/// <summary>
/// A fire-and-forget pipeline that silently drops data is a liability, so every
/// outcome is counted. Alert on <c>api_logs.dropped</c> and <c>api_logs.failed</c>,
/// and watch <c>api_logs.queue_depth</c> for a database that cannot keep up.
/// View locally with: dotnet-counters monitor -n api-logging-channels --counters ApiLogging
/// </summary>
public sealed class ApiLoggingMetrics
{
    public const string MeterName = "ApiLogging";

    private readonly Meter _meter;
    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _dropped;
    private readonly Counter<long> _written;
    private readonly Counter<long> _failed;
    private readonly Histogram<int> _batchSize;
    private readonly Histogram<double> _flushDuration;

    public ApiLoggingMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _enqueued = _meter.CreateCounter<long>("api_logs.enqueued", description: "Entries accepted into the in-memory queue.");
        _dropped = _meter.CreateCounter<long>("api_logs.dropped", description: "Entries rejected because the queue was full or closed.");
        _written = _meter.CreateCounter<long>("api_logs.written", description: "Entries persisted to the database.");
        _failed = _meter.CreateCounter<long>("api_logs.failed", description: "Entries lost after all write retries failed.");
        _batchSize = _meter.CreateHistogram<int>("api_logs.batch_size", description: "Entries per database write.");
        _flushDuration = _meter.CreateHistogram<double>("api_logs.flush_duration", unit: "ms", description: "Time to write one batch.");
    }

    public void RegisterQueueDepth(Func<int> observe) =>
        _meter.CreateObservableGauge("api_logs.queue_depth", observe, description: "Entries waiting to be written.");

    public void Enqueued() => _enqueued.Add(1);
    public void Dropped() => _dropped.Add(1);
    public void Failed(int count) => _failed.Add(count);

    public void Written(int count, TimeSpan elapsed)
    {
        _written.Add(count);
        _batchSize.Record(count);
        _flushDuration.Record(elapsed.TotalMilliseconds);
    }
}
