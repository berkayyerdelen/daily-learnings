using Microsoft.Data.SqlClient;

namespace ApiLogging.Api.Logging;

public interface IApiLogRepository
{
    /// <summary>Inserts all logs atomically: either the whole batch is stored or none of it.</summary>
    Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken);

    /// <summary>True if the failure is temporary and the same batch may succeed on retry.</summary>
    bool IsTransient(Exception exception);
}

/// <summary>
/// ADO.NET batch insert with SqlBulkCopy: one round trip per batch, much faster than
/// one INSERT per row (and no 1,000-row / 2,100-parameter limit of multi-row INSERTs).
///
/// Rows are streamed through <see cref="ApiLogDataReader"/>: no DataTable copy of the batch,
/// and no shared state, so several writers can insert in parallel.
/// </summary>
public sealed class SqlApiLogRepository(string connectionString) : IApiLogRepository
{
    /// <summary>
    /// SQL Server errors worth retrying: the same batch can succeed a moment later.
    /// Anything else (bad schema, permissions, a row that does not fit a column, a missing
    /// database) fails the same way every time, so retrying only delays the next batch.
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        -2,     // client timeout
        1205,   // chosen as deadlock victim (whole batch rolled back, so retry is safe)
        1222,   // lock request timeout
        233,    // connection closed by the server
        64,     // connection lost
        10053,  // connection aborted by the host
        10054,  // connection reset by peer
        10060,  // connection attempt timed out
        10928, 10929, // Azure SQL: resource limits reached
        40197,  // Azure SQL: service error while processing (e.g. during failover)
        40501,  // Azure SQL: service busy
        40613,  // Azure SQL: database unavailable (failover in progress)
        49918, 49919, 49920 // Azure SQL: not enough resources / too many operations
    ];

    public bool IsTransient(Exception exception) => exception switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(e => TransientErrorNumbers.Contains(e.Number)),
        TimeoutException => true,
        _ => false
    };

    // Stateless: safe to call from several writers at once.
    public async Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString); // pooled
        await connection.OpenAsync(cancellationToken);

        // No TableLock: with a clustered index it takes an exclusive table lock, so parallel
        // writers and other app instances would take turns instead of inserting concurrently.
        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = "dbo.ApiLogs",
            BatchSize = 0,          // whole batch in one transaction: all rows or none
            BulkCopyTimeout = 60
            // EnableStreaming stays off. Streaming sends every nvarchar(max) value as separate async
            // chunks; under HTTP load each chunk waits for a busy thread-pool thread, so one batch took
            // 44 s while SQL Server sat in ASYNC_NETWORK_IO holding its locks and blocking the other
            // writers. It only helps when the source is itself a streaming database reader; our rows
            // are already in memory.
        };
        foreach (var column in ApiLogDataReader.Columns)
            bulkCopy.ColumnMappings.Add(column, column); // Id is identity, skipped

        await using var reader = new ApiLogDataReader(logs);
        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
    }
}
