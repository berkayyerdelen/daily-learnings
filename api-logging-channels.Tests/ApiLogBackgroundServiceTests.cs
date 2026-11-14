using System.Threading.Channels;
using ApiLogging.Api.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiLogging.Tests;

public sealed class ApiLogBackgroundServiceTests
{
    private readonly FakeApiLogRepository _repository = new();
    private readonly Channel<ApiLog> _channel = Channel.CreateBounded<ApiLog>(1_000);
    private readonly ApiLogStats _stats = new();

    private ApiLogBackgroundService Create(int batchSize, TimeSpan flushInterval, int maxRetries = 3, int writers = 1) => new(
        _channel.Reader, _repository, _stats,
        Options.Create(new ApiLogOptions
        {
            BatchSize = batchSize,
            FlushInterval = flushInterval,
            MaxRetries = maxRetries,
            WriterCount = writers,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1) // keep tests fast
        }),
        NullLogger<ApiLogBackgroundService>.Instance);

    [Fact]
    public async Task Inserts_full_batches_and_drains_the_rest_on_shutdown()
    {
        var service = Create(batchSize: 10, flushInterval: TimeSpan.FromMinutes(1));
        for (var i = 0; i < 25; i++) _channel.Writer.TryWrite(Log(i));

        await service.StartAsync(default);
        await _repository.WaitForAsync(20);
        await service.StopAsync(default);

        Assert.Equal(new[] { 10, 10, 5 }, _repository.Batches.Select(b => b.Length));
    }

    [Fact]
    public async Task Inserts_a_partial_batch_after_the_flush_interval()
    {
        var service = Create(batchSize: 1_000, flushInterval: TimeSpan.FromMilliseconds(50));
        await service.StartAsync(default);

        _channel.Writer.TryWrite(Log(1));
        _channel.Writer.TryWrite(Log(2));

        await _repository.WaitForAsync(2);
        Assert.Single(_repository.Batches);
        await service.StopAsync(default);
    }

    [Fact]
    public async Task Retries_a_temporary_failure_and_then_inserts()
    {
        _repository.FailAttempt = a => a <= 2 ? new TimeoutException("blip") : null;
        var service = Create(batchSize: 10, flushInterval: TimeSpan.FromMilliseconds(20));
        await service.StartAsync(default);

        _channel.Writer.TryWrite(Log(1));

        await _repository.WaitForAsync(1);
        await service.StopAsync(default);
        Assert.Equal(3, _repository.Attempts);
        Assert.Equal(1, _stats.Inserted);
        Assert.Equal(0, _stats.Failed);
    }

    [Fact]
    public async Task Does_not_retry_a_permanent_failure()
    {
        _repository.FailAttempt = a => a == 1 ? new InvalidOperationException("bad schema") : null;
        var service = Create(batchSize: 10, flushInterval: TimeSpan.FromMilliseconds(20));
        await service.StartAsync(default);

        _channel.Writer.TryWrite(Log(1));
        await WaitUntil(() => _stats.Failed == 1);
        _channel.Writer.TryWrite(Log(2)); // the next batch is not held up

        await _repository.WaitForAsync(1);
        await service.StopAsync(default);
        Assert.Equal(2, _repository.Attempts);
        Assert.Equal("/2", Assert.Single(_repository.Logs).Path);
    }

    [Fact]
    public async Task Drops_the_batch_after_MaxRetries()
    {
        _repository.FailAttempt = _ => new TimeoutException("still down");
        var service = Create(batchSize: 10, flushInterval: TimeSpan.FromMilliseconds(20), maxRetries: 2);
        await service.StartAsync(default);

        // Enqueue the way ApiLogFilter does: reserve the memory budget first.
        var log = Log(1);
        Assert.True(_stats.TryReserve(log.ApproximateBytes, long.MaxValue));
        _channel.Writer.TryWrite(log);

        await WaitUntil(() => _stats.Failed == 1);
        await service.StopAsync(default);
        Assert.Equal(3, _repository.Attempts); // 1 try + 2 retries
        Assert.Equal(0, _stats.Inserted);
        Assert.Equal(0, _stats.QueuedBytes);   // memory released even though the batch was dropped
    }

    [Fact]
    public async Task Parallel_writers_insert_every_log_exactly_once()
    {
        var channel = Channel.CreateBounded<ApiLog>(10_000);
        var service = new ApiLogBackgroundService(channel.Reader, _repository, _stats,
            Options.Create(new ApiLogOptions { BatchSize = 7, FlushInterval = TimeSpan.FromMilliseconds(20), WriterCount = 4 }),
            NullLogger<ApiLogBackgroundService>.Instance);
        await service.StartAsync(default);

        for (var i = 0; i < 5_000; i++) Assert.True(channel.Writer.TryWrite(Log(i)));
        await service.StopAsync(default); // drains with all writers

        Assert.Equal(5_000, _repository.Logs.Select(l => l.Path).Distinct().Count());
        Assert.Equal(5_000, _repository.Logs.Count);
        Assert.All(_repository.Batches, b => Assert.InRange(b.Length, 1, 7));
        Assert.Equal(0, _stats.InFlight);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, cts.Token);
    }

    private static ApiLog Log(int i) =>
        new(DateTime.UtcNow, $"t{i}", "GET", $"/{i}", null, 200, 1, null, null, null);
}
