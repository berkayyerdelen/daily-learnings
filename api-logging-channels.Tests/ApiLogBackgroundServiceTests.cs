using System.Threading.Channels;
using ApiLogging.Api.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiLogging.Tests;

public sealed class ApiLogBackgroundServiceTests
{
    private readonly FakeApiLogRepository _repository = new();
    private readonly Channel<ApiLog> _channel = Channel.CreateBounded<ApiLog>(1_000);
    private readonly ApiLogMemoryBudget _budget = new(Options.Create(new ApiLogOptions()));

    private ApiLogBackgroundService Create(int batchSize, TimeSpan flushInterval, int maxRetries = 3, int writers = 1,
        ChannelReader<ApiLog>? reader = null, TimeSpan? maxFailureCooldown = null, TimeSpan? shutdownTimeout = null) => new(
        reader ?? _channel.Reader, _repository, _budget,
        Options.Create(new ApiLogOptions
        {
            BatchSize = batchSize,
            FlushInterval = flushInterval,
            MaxRetries = maxRetries,
            WriterCount = writers,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1), // keep tests fast
            MaxFailureCooldown = maxFailureCooldown ?? TimeSpan.Zero
        }),
        Options.Create(new HostOptions { ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(30) }),
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
        Assert.Single(_repository.Logs);
    }

    [Fact]
    public async Task Does_not_retry_a_permanent_failure()
    {
        _repository.FailAttempt = a => a == 1 ? new InvalidOperationException("bad schema") : null;
        var service = Create(batchSize: 10, flushInterval: TimeSpan.FromMilliseconds(20));
        await service.StartAsync(default);

        _channel.Writer.TryWrite(Log(1));
        await WaitUntil(() => _repository.Attempts == 1);
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
        Assert.True(_budget.TryReserve(log.ApproximateBytes));
        _channel.Writer.TryWrite(log);

        await WaitUntil(() => _repository.Attempts == 3);
        await service.StopAsync(default);
        Assert.Equal(3, _repository.Attempts); // 1 try + 2 retries
        Assert.Empty(_repository.Logs);
        Assert.Equal(0, _budget.UsedBytes);    // memory released even though the batch was dropped
    }

    [Fact]
    public async Task Parallel_writers_insert_every_log_exactly_once()
    {
        var channel = Channel.CreateBounded<ApiLog>(10_000);
        var service = new ApiLogBackgroundService(channel.Reader, _repository, _budget,
            Options.Create(new ApiLogOptions { BatchSize = 7, FlushInterval = TimeSpan.FromMilliseconds(20), WriterCount = 4 }),
            Options.Create(new HostOptions()),
            NullLogger<ApiLogBackgroundService>.Instance);
        await service.StartAsync(default);

        for (var i = 0; i < 5_000; i++) Assert.True(channel.Writer.TryWrite(Log(i)));
        await service.StopAsync(default); // drains with all writers

        Assert.Equal(5_000, _repository.Logs.Select(l => l.Path).Distinct().Count());
        Assert.Equal(5_000, _repository.Logs.Count);
        Assert.All(_repository.Batches, b => Assert.InRange(b.Length, 1, 7));
    }

    [Fact]
    public async Task A_crashing_writer_restarts_instead_of_stopping_the_host_and_releases_its_memory()
    {
        // The reader throws on its 2nd WaitToReadAsync: the writer has already taken log 1 into its
        // batch, so the crash happens mid-batch (a bug path, not a database error).
        var reader = new ThrowingReader(_channel.Reader, throwOnCall: 2);
        var service = Create(batchSize: 10, flushInterval: TimeSpan.FromSeconds(30), reader: reader);
        await service.StartAsync(default);

        var first = Log(1);
        Assert.True(_budget.TryReserve(first.ApproximateBytes));
        _channel.Writer.TryWrite(first);
        await WaitUntil(() => reader.Calls >= 2);
        await WaitUntil(() => _budget.UsedBytes == 0); // the crashed batch's memory was released

        // The writer was restarted (after ~1 s) and keeps working.
        _channel.Writer.TryWrite(Log(2));
        await service.StopAsync(default);
        Assert.Equal("/2", Assert.Single(_repository.Logs).Path);
        Assert.False(service.ExecuteTask!.IsFaulted); // never surfaced to the host
    }

    [Fact]
    public async Task Shutdown_with_a_hung_database_finishes_within_the_host_shutdown_timeout()
    {
        // Every insert hangs until cancelled, like a database that accepts connections but never answers.
        var hung = new HangingRepository();
        var service = new ApiLogBackgroundService(_channel.Reader, hung, _budget,
            Options.Create(new ApiLogOptions { BatchSize = 10, FlushInterval = TimeSpan.FromMilliseconds(20) }),
            Options.Create(new HostOptions { ShutdownTimeout = TimeSpan.FromSeconds(4) }), // drain deadline = 2 s
            NullLogger<ApiLogBackgroundService>.Instance);
        await service.StartAsync(default);
        for (var i = 0; i < 25; i++) _channel.Writer.TryWrite(Log(i));
        await WaitUntil(() => hung.Calls >= 1);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var hostGivesUp = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await service.StopAsync(hostGivesUp.Token);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(1)); // writers really finished

        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task A_down_database_is_not_hammered_thanks_to_the_cooldown()
    {
        _repository.FailAttempt = _ => new InvalidOperationException("database down");
        var service = Create(batchSize: 1, flushInterval: TimeSpan.FromMilliseconds(5), maxRetries: 0,
            maxFailureCooldown: TimeSpan.FromSeconds(10));
        await service.StartAsync(default);

        // 200 one-log batches ready to go. Without a cooldown, that's ~200 attempts in a second.
        for (var i = 0; i < 200; i++) _channel.Writer.TryWrite(Log(i));
        await Task.Delay(1_000);

        // base 1 ms doubling per failure: 1, 2, 4 ... 512 ms. Roughly 10 attempts fit in a second.
        Assert.InRange(_repository.Attempts, 2, 15);
        await service.StopAsync(default);
    }

    private sealed class ThrowingReader(ChannelReader<ApiLog> inner, int throwOnCall) : ChannelReader<ApiLog>
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public override bool CanCount => true;
        public override int Count => inner.Count;
        public override bool TryRead(out ApiLog item) => inner.TryRead(out item!);

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _calls) == throwOnCall
                ? throw new InvalidOperationException("simulated bug")
                : inner.WaitToReadAsync(cancellationToken);
    }

    private sealed class HangingRepository : IApiLogRepository
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public async Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public bool IsTransient(Exception exception) => true; // would retry forever if allowed
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, cts.Token);
    }

    private static ApiLog Log(int i) =>
        new(DateTime.UtcNow, $"t{i}", "GET", $"/{i}", null, 200, 1, null, null, null);
}
