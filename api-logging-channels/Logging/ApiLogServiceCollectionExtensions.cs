using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

public static class ApiLogServiceCollectionExtensions
{
    public static IServiceCollection AddApiLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiLogOptions>(configuration.GetSection("ApiLogging"));

        // One bounded channel shared by the filter (many writers) and the background service (one reader).
        services.AddSingleton(sp => Channel.CreateBounded<ApiLog>(
            new BoundedChannelOptions(sp.GetRequiredService<IOptions<ApiLogOptions>>().Value.Capacity)
            {
                // With Wait, TryWrite returns false when full instead of blocking: the log is dropped.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            }));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<ApiLog>>().Writer);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<ApiLog>>().Reader);

        services.TryAddSingleton<IApiLogRepository>(_ => new SqlApiLogRepository(
            configuration.GetConnectionString("ApiLogs")
            ?? throw new InvalidOperationException("Connection string 'ApiLogs' is missing.")));

        services.AddSingleton<ApiLogStats>();
        services.AddHostedService<ApiLogBackgroundService>();

        services.AddSingleton<ApiLogFilter>();
        services.Configure<MvcOptions>(o => o.Filters.AddService<ApiLogFilter>(ApiLogFilter.Order));

        return services;
    }

    /// <summary>GET /api-logs/stats: queue depth and pipeline counters.</summary>
    public static IEndpointRouteBuilder MapApiLogStats(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api-logs/stats", (ApiLogStats stats, ChannelReader<ApiLog> reader, IOptions<ApiLogOptions> options) =>
            new
            {
                queued = reader.Count,          // waiting in memory right now
                capacity = options.Value.Capacity,
                queuedBytes = stats.QueuedBytes, // approx. memory held by queued + in-flight logs
                maxQueueBytes = options.Value.MaxQueueBytes,
                enqueued = stats.Enqueued,      // accepted by the filter
                dropped = stats.Dropped,        // rejected because the queue was full
                inserted = stats.Inserted,      // written to the database
                failed = stats.Failed,          // lost because a batch insert failed
                batches = stats.Batches,
                // Process memory, to spot leaks during load tests. gen2SizeAfterGcBytes (objects that
                // survived to the oldest generation) is the one to watch: it must plateau, not climb.
                gen2SizeAfterGcBytes = GC.GetGCMemoryInfo().GenerationInfo[2].SizeAfterBytes,
                gcHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
                workingSetBytes = Environment.WorkingSet,
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2)
            });
        return app;
    }
}
