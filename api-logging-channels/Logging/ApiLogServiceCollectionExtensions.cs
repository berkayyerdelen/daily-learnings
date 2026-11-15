using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

public static class ApiLogServiceCollectionExtensions
{
    public static IServiceCollection AddApiLogging(this IServiceCollection services, IConfiguration configuration)
    {
        // Invalid settings stop the app at startup with a clear message instead of failing at runtime.
        services.AddOptions<ApiLogOptions>()
            .Bind(configuration.GetSection("ApiLogging"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One bounded channel shared by the filter (many writers) and the background service (one reader).
        services.AddSingleton(sp => Channel.CreateBounded<ApiLog>(
            new BoundedChannelOptions(sp.GetRequiredService<IOptions<ApiLogOptions>>().Value.Capacity)
            {
                // With Wait, TryWrite returns false when full instead of blocking: the log is dropped.
                FullMode = BoundedChannelFullMode.Wait,
                // One reader enables a faster channel implementation; parallel writers need many.
                SingleReader = sp.GetRequiredService<IOptions<ApiLogOptions>>().Value.WriterCount <= 1,
                SingleWriter = false
            }));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<ApiLog>>().Writer);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<ApiLog>>().Reader);

        services.TryAddSingleton<IApiLogRepository>(_ => new SqlApiLogRepository(
            configuration.GetConnectionString("ApiLogs")
            ?? throw new InvalidOperationException("Connection string 'ApiLogs' is missing.")));

        services.AddSingleton<ApiLogMemoryBudget>();
        services.AddHostedService<ApiLogBackgroundService>();

        services.AddSingleton<ApiLogFilter>();
        services.Configure<MvcOptions>(o => o.Filters.AddService<ApiLogFilter>(ApiLogFilter.Order));

        return services;
    }
}
