using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Creates the api_logs table on startup when <see cref="ApiLoggingOptions.EnsureSchema"/>
/// is on. Registered before <see cref="ApiLogBatchWriter"/>, and hosted services start in
/// order, so the table exists before the first flush.
/// </summary>
internal sealed class ApiLogSchemaInitializer(
    IApiLogStore store,
    IOptions<ApiLoggingOptions> options,
    ILogger<ApiLogSchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !options.Value.EnsureSchema) return;

        await store.EnsureSchemaAsync(cancellationToken);
        logger.LogInformation("api_logs schema ensured");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
