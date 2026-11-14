using System.Collections.Concurrent;
using ApiLogging.Api.Logging;

namespace ApiLogging.Tests;

internal sealed class FakeApiLogRepository : IApiLogRepository
{
    private readonly ConcurrentQueue<ApiLog[]> _batches = new();

    public IReadOnlyList<ApiLog[]> Batches => _batches.ToArray();
    public IReadOnlyList<ApiLog> Logs => _batches.SelectMany(b => b).ToArray();

    public Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken)
    {
        _batches.Enqueue(logs.ToArray()); // copy: the service reuses its list
        return Task.CompletedTask;
    }

    public async Task WaitForAsync(int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Logs.Count < count) await Task.Delay(10, cts.Token);
    }
}
