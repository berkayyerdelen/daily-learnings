using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ApiLogging.Api.Logging;

public static class ApiLoggingServiceCollectionExtensions
{
    public const string ConnectionStringName = "ApiLogs";

    public static IServiceCollection AddApiLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiLoggingOptions>()
            .Bind(configuration.GetSection(ApiLoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ApiLoggingMetrics>();
        services.AddSingleton<ApiLogQueue>();
        services.AddSingleton<ApiLogActionFilter>(); // stateless; one instance serves all requests

        // TryAdd so a host or test can supply its own store.
        services.TryAddSingleton<IApiLogStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ApiLoggingOptions>>();
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is required for API logging.");

            return options.Value.Provider switch
            {
                ApiLogProvider.SqlServer => new SqlServerApiLogStore(connectionString, options),
                // NpgsqlDataSource owns the pool; the store is a singleton, so it is created once.
                ApiLogProvider.Postgres => new PostgresApiLogStore(NpgsqlDataSource.Create(connectionString), options),
                _ => throw new InvalidOperationException($"Unknown API log provider '{options.Value.Provider}'.")
            };
        });

        // Order matters: hosted services start in registration order.
        services.AddHostedService<ApiLogSchemaInitializer>();
        services.AddHostedService<ApiLogBatchWriter>();

        services.Configure<MvcOptions>(mvc =>
            mvc.Filters.AddService<ApiLogActionFilter>(ApiLogActionFilter.FilterOrder));

        return services;
    }
}
