using System.Diagnostics;
using System.Threading.Channels;
using ApiLogging.Api.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiLogging.LoadTests;

/// <summary>
/// Pushes logs into the same channel / ApiLogBackgroundService / SqlApiLogRepository the API
/// uses, at a fixed rate, and reports how many rows per second actually reach the database.
/// No HTTP involved, so a laptop can generate 100k+ logs/s and the database is what is measured.
/// </summary>
internal static class DbLoadTest
{
    public static async Task<int> RunAsync(Func<string, string, string> arg)
    {
        var rate = int.Parse(arg("--rps", "100000"));
        var duration = TimeSpan.FromSeconds(int.Parse(arg("--duration", "30")));
        var bodySize = int.Parse(arg("--body-size", "500"));
        var connection = arg("--connection", "");
        var options = new ApiLogOptions
        {
            Capacity = int.Parse(arg("--capacity", "100000")),
            BatchSize = int.Parse(arg("--batch-size", "5000")),
            FlushInterval = TimeSpan.FromSeconds(1)
        };

        IApiLogRepository repository = connection.Length == 0 ? new NoOpRepository() : new SqlApiLogRepository(connection);
        Console.WriteLine($"db mode: target {rate:N0} logs/s for {duration.TotalSeconds}s, body {bodySize} chars, " +
                          $"batch {options.BatchSize:N0}, capacity {options.Capacity:N0}, " +
                          (connection.Length == 0 ? "NO database (pipeline only)" : "SQL Server"));

        var channel = Channel.CreateBounded<ApiLog>(new BoundedChannelOptions(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        var stats = new ApiLogStats();
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        var service = new ApiLogBackgroundService(channel.Reader, repository, stats, Options.Create(options),
            loggerFactory.CreateLogger<ApiLogBackgroundService>());
        await service.StartAsync(CancellationToken.None);

        var request = $$"""{"customerId":"c-42","notes":"{{new string('x', bodySize)}}"}""";
        var response = """{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","customerId":"c-42","amount":10.5}""";

        // Producer: paced in 10 ms steps so the target rate is spread evenly over each second.
        long produced = 0;
        var clock = Stopwatch.StartNew();
        var producer = Task.Run(async () =>
        {
            while (clock.Elapsed < duration)
            {
                var target = (long)(rate * clock.Elapsed.TotalSeconds);
                for (; produced < target; produced++)
                {
                    var log = new ApiLog(DateTime.UtcNow, Guid.NewGuid().ToString("N"), "POST", "/api/orders", null,
                        201, 2, request, response, null);

                    // Same admission rules as ApiLogFilter: byte budget, then non-blocking TryWrite.
                    if (stats.TryReserve(log.ApproximateBytes, options.MaxQueueBytes))
                    {
                        if (channel.Writer.TryWrite(log)) { stats.OnEnqueued(); continue; }
                        stats.Release(log.ApproximateBytes);
                    }
                    stats.OnDropped();
                }
                await Task.Delay(10);
            }
        });

        Console.WriteLine();
        Console.WriteLine($"{"sec",4} {"produced/s",11} {"inserted/s",11} | {"queued",8} {"dropped",9} {"failed",8} {"inserted",11}");
        long lastProduced = 0, lastInserted = 0;
        while (!producer.IsCompleted)
        {
            await Task.WhenAny(producer, Task.Delay(1000));
            var p = Interlocked.Read(ref produced);
            var i = stats.Inserted;
            Console.WriteLine($"{(int)clock.Elapsed.TotalSeconds,4} {p - lastProduced,11:N0} {i - lastInserted,11:N0} | " +
                              $"{channel.Reader.Count,8:N0} {stats.Dropped,9:N0} {stats.Failed,8:N0} {i,11:N0}");
            lastProduced = p;
            lastInserted = i;
        }

        // Stop = drain: the service writes everything still queued, like on app shutdown.
        await service.StopAsync(CancellationToken.None);
        var total = clock.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine($"Produced {produced:N0}  enqueued {stats.Enqueued:N0}  dropped {stats.Dropped:N0} " +
                          $"({100.0 * stats.Dropped / Math.Max(1, produced):F1}%)  inserted {stats.Inserted:N0}  failed {stats.Failed:N0}");
        Console.WriteLine($"Sustained insert rate: {stats.Inserted / total:N0} rows/s over {total:F1}s (including drain)");
        Console.WriteLine(stats.Dropped == 0
            ? $"OK: the pipeline kept up with {rate:N0} logs/s."
            : "Dropped logs: the database did not keep up with this rate. Try a larger --batch-size, or see README.");
        return 0;
    }

    private sealed class NoOpRepository : IApiLogRepository
    {
        public Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
