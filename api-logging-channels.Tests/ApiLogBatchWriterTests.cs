using System.Diagnostics.Metrics;
using ApiLogging.Api.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiLogging.Tests;

public sealed class ApiLogBatchWriterTests : IAsyncLifetime
{
    private readonly InMemoryApiLogStore _store = new();
    private readonly ServiceProvider _metricsServices = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private ApiLogQueue _queue = null!;
    private ApiLogBatchWriter _writer = null!;

    private void Create(Action<ApiLoggingOptions>? configure = null)
    {
        var options = new ApiLoggingOptions
        {
            ChannelCapacity = 1_000,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(100),
            MaxRetries = 2
        };
        configure?.Invoke(options);

        var wrapped = Options.Create(options);
        var metrics = new ApiLoggingMetrics(_metricsServices.GetRequiredService<IMeterFactory>());
        _queue = new ApiLogQueue(wrapped, metrics);
        _writer = new ApiLogBatchWriter(_queue, _store, metrics, wrapped, TimeProvider.System,
            NullLogger<ApiLogBatchWriter>.Instance);
    }

    [Fact]
    public async Task Splits_entries_into_batches_of_BatchSize()
    {
        Create(o => o.FlushInterval = TimeSpan.FromSeconds(30));
        for (var i = 0; i < 25; i++) _queue.TryEnqueue(Entry(i));

        await _writer.StartAsync(default);
        await _store.WaitForEntriesAsync(20);

        Assert.All(_store.Batches, b => Assert.Equal(10, b.Count));

        // The remaining 5 are below BatchSize and the interval is long: they wait...
        await Task.Delay(200);
        Assert.Equal(20, _store.Entries.Count);

        // ...until shutdown drains them.
        await _writer.StoppedAsync(default);
        Assert.Equal(new[] { 10, 10, 5 }, _store.Batches.Select(b => b.Count));
    }

    [Fact]
    public async Task Flushes_a_partial_batch_after_FlushInterval()
    {
        Create(o => o.BatchSize = 1_000);
        await _writer.StartAsync(default);

        _queue.TryEnqueue(Entry(1));
        _queue.TryEnqueue(Entry(2));

        await _store.WaitForEntriesAsync(2);
        Assert.Single(_store.Batches);
    }

    [Fact]
    public async Task Drains_everything_on_shutdown_and_rejects_later_entries()
    {
        Create(o => o.FlushInterval = TimeSpan.FromMinutes(1));
        await _writer.StartAsync(default);
        for (var i = 0; i < 3; i++) _queue.TryEnqueue(Entry(i));

        await _writer.StoppedAsync(default);

        Assert.Equal(3, _store.Entries.Count);
        Assert.False(_queue.TryEnqueue(Entry(99)));
    }

    [Fact]
    public void Drops_instead_of_blocking_when_the_queue_is_full()
    {
        Create(o => o.ChannelCapacity = 2);

        Assert.True(_queue.TryEnqueue(Entry(1)));
        Assert.True(_queue.TryEnqueue(Entry(2)));
        Assert.False(_queue.TryEnqueue(Entry(3)));
    }

    [Fact]
    public async Task Retries_a_failed_batch()
    {
        Create();
        _store.BeforeWrite = attempt => attempt <= 2 ? throw new TimeoutException("db down") : Task.CompletedTask;
        await _writer.StartAsync(default);

        _queue.TryEnqueue(Entry(1));

        await _store.WaitForEntriesAsync(1);
        Assert.Equal(3, _store.Attempts);
    }

    [Fact]
    public async Task Drops_a_batch_after_retries_are_exhausted_and_keeps_going()
    {
        Create(o => o.MaxRetries = 1);
        // Attempts 1 and 2 fail (first batch is dropped); attempt 3 succeeds.
        _store.BeforeWrite = attempt => attempt <= 2 ? throw new TimeoutException("db down") : Task.CompletedTask;
        await _writer.StartAsync(default);

        _queue.TryEnqueue(Entry(1));
        while (_store.Attempts < 2) await Task.Delay(10);
        _queue.TryEnqueue(Entry(2));

        await _store.WaitForEntriesAsync(1);
        Assert.Equal("/2", Assert.Single(_store.Entries).Path);
    }

    [Fact]
    public async Task Parallel_writers_write_every_entry_exactly_once()
    {
        Create(o => { o.WriterConcurrency = 4; o.BatchSize = 7; });
        _store.BeforeWrite = _ => Task.Delay(5); // simulate DB latency so writers overlap
        await _writer.StartAsync(default);

        for (var i = 0; i < 500; i++) Assert.True(_queue.TryEnqueue(Entry(i)));
        await _writer.StoppedAsync(default);

        Assert.Equal(500, _store.Entries.Select(e => e.Path).Distinct().Count());
        Assert.All(_store.Batches, b => Assert.InRange(b.Count, 1, 7));
    }

    private static ApiLogEntry Entry(int i) => new()
    {
        OccurredAt = DateTimeOffset.UtcNow,
        TraceId = $"trace-{i}",
        HttpMethod = "GET",
        Path = $"/{i}",
        StatusCode = 200,
        DurationMs = 1
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_writer is not null) await _writer.StoppedAsync(default);
        await _metricsServices.DisposeAsync();
    }
}
