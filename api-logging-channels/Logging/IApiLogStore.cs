namespace ApiLogging.Api.Logging;

/// <summary>
/// Persistence seam. The batching pipeline knows nothing about the database, which keeps
/// it testable and lets the store be swapped (e.g. SQL Server with SqlBulkCopy).
/// </summary>
public interface IApiLogStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    /// <summary>Write all entries atomically: either the whole batch is stored or none of it.</summary>
    Task WriteBatchAsync(IReadOnlyList<ApiLogEntry> batch, CancellationToken cancellationToken);
}
