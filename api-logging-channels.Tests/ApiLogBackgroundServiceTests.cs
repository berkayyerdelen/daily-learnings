using System.Threading.Channels;
using ApiLogging.Api.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiLogging.Tests;

public sealed class ApiLogBackgroundServiceTests
{
    private readonly FakeApiLogRepository _repository = new();
    private readonly Channel<ApiLog> _channel = Channel.CreateBounded<ApiLog>(1_000);

    private ApiLogBackgroundService Create(int batchSize, TimeSpan flushInterval) => new(
        _channel.Reader, _repository, new ApiLogStats(),
        Options.Create(new ApiLogOptions { BatchSize = batchSize, FlushInterval = flushInterval }),
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

    private static ApiLog Log(int i) =>
        new(DateTime.UtcNow, $"t{i}", "GET", $"/{i}", null, 200, 1, null, null, null);
}
