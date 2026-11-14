using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Reads logs from the channel and inserts them in batches: when BatchSize logs are
/// collected, or when FlushInterval has passed, whichever comes first. On shutdown it
/// writes whatever is left in the channel.
/// </summary>
public sealed class ApiLogBackgroundService(
    ChannelReader<ApiLog> reader,
    IApiLogRepository repository,
    ApiLogStats stats,
    IOptions<ApiLogOptions> options,
    ILogger<ApiLogBackgroundService> logger) : BackgroundService
{
    private readonly ApiLogOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiLog>(_options.BatchSize);

        try
        {
            // Wait (without polling) until at least one log arrives.
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                await FillBatchAsync(batch, stoppingToken);
                await FlushAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Shutdown: drain what is left.
        while (reader.TryRead(out var log))
        {
            batch.Add(log);
            if (batch.Count >= _options.BatchSize) await FlushAsync(batch);
        }
        await FlushAsync(batch);
    }

    private async Task FillBatchAsync(List<ApiLog> batch, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(_options.FlushInterval);

        try
        {
            while (batch.Count < _options.BatchSize)
            {
                while (batch.Count < _options.BatchSize && reader.TryRead(out var log))
                    batch.Add(log);

                if (batch.Count < _options.BatchSize)
                    await reader.WaitToReadAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Flush interval elapsed (or shutting down): insert what we have.
        }
    }

    private async Task FlushAsync(List<ApiLog> batch)
    {
        if (batch.Count == 0) return;

        try
        {
            // Not tied to stoppingToken, so a batch in progress at shutdown still completes.
            await repository.InsertBatchAsync(batch, CancellationToken.None);
            stats.OnBatchInserted(batch.Count);
        }
        catch (Exception ex)
        {
            stats.OnBatchFailed(batch.Count);
            logger.LogError(ex, "Failed to insert {Count} API logs", batch.Count);
        }
        finally
        {
            long bytes = 0;
            foreach (var log in batch) bytes += log.ApproximateBytes;
            stats.Release(bytes);
            batch.Clear(); // keeps the list's capacity: no new allocation per batch
        }
    }
}
