using System.Collections.Concurrent;
using ApiLogging.Api.Logging;

namespace ApiLogging.Tests;

internal sealed class InMemoryApiLogStore : IApiLogStore
{
    private readonly ConcurrentQueue<IReadOnlyList<ApiLogEntry>> _batches = new();

    /// <summary>Called before each write; throw from it to simulate a database failure.</summary>
    public Func<int, Task>? BeforeWrite { get; set; }

    public int Attempts;

    public IReadOnlyList<IReadOnlyList<ApiLogEntry>> Batches => _batches.ToArray();
    public IReadOnlyList<ApiLogEntry> Entries => _batches.SelectMany(b => b).ToArray();

    public Task EnsureSchemaAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WriteBatchAsync(IReadOnlyList<ApiLogEntry> batch, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref Attempts);
        if (BeforeWrite is not null) await BeforeWrite(attempt);
        // Copy: the writer reuses its list after a flush.
        _batches.Enqueue(batch.ToArray());
    }

    public async Task WaitForEntriesAsync(int count, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (Entries.Count < count)
            await Task.Delay(10, cts.Token);
    }
}
