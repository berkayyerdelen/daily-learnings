using System.Collections.Concurrent;
using ApiLogging.Api.Logging;

namespace ApiLogging.Tests;

internal sealed class FakeApiLogRepository : IApiLogRepository
{
    private readonly ConcurrentQueue<ApiLog[]> _batches = new();

    private int _attempts;

    /// <summary>Called with the attempt number (1, 2, ...); return an exception to fail that attempt.</summary>
    public Func<int, Exception?> FailAttempt { get; set; } = _ => null;

    public int Attempts => Volatile.Read(ref _attempts);

    public IReadOnlyList<ApiLog[]> Batches => _batches.ToArray();
    public IReadOnlyList<ApiLog> Logs => _batches.SelectMany(b => b).ToArray();

    public Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken)
    {
        var failure = FailAttempt(Interlocked.Increment(ref _attempts));
        if (failure is not null) throw failure;

        _batches.Enqueue(logs.ToArray()); // copy: the service reuses its list
        return Task.CompletedTask;
    }

    public bool IsTransient(Exception exception) => exception is TimeoutException;

    public async Task WaitForAsync(int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Logs.Count < count) await Task.Delay(10, cts.Token);
    }
}
