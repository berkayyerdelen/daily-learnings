using System.Data;
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
/// The DataTable is created once and cleared after each batch, so its schema and internal
/// storage are reused instead of reallocated. This is safe because exactly one background
/// service calls this, never concurrently.
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

    private readonly DataTable _table = CreateTable();

    public bool IsTransient(Exception exception) => exception switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(e => TransientErrorNumbers.Contains(e.Number)),
        TimeoutException => true,
        _ => false
    };

    public async Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var l in logs)
                _table.Rows.Add(l.CreatedAt, l.TraceId, l.Method, l.Path, l.QueryString, l.StatusCode,
                    l.DurationMs, l.RequestBody, l.ResponseBody, l.Exception);

            await using var connection = new SqlConnection(connectionString); // pooled
            await connection.OpenAsync(cancellationToken);

            // No TableLock: with a clustered index it would block every other app instance.
            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = "dbo.ApiLogs",
                BatchSize = 0,          // whole batch in one transaction
                BulkCopyTimeout = 60
            };
            foreach (DataColumn column in _table.Columns)
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName); // Id is identity, skipped

            await bulkCopy.WriteToServerAsync(_table, cancellationToken);
        }
        finally
        {
            _table.Clear(); // release row references so bodies can be collected
        }
    }

    private static DataTable CreateTable()
    {
        var table = new DataTable();
        table.Columns.Add(nameof(ApiLog.CreatedAt), typeof(DateTime));
        table.Columns.Add(nameof(ApiLog.TraceId), typeof(string));
        table.Columns.Add(nameof(ApiLog.Method), typeof(string));
        table.Columns.Add(nameof(ApiLog.Path), typeof(string));
        table.Columns.Add(nameof(ApiLog.QueryString), typeof(string));
        table.Columns.Add(nameof(ApiLog.StatusCode), typeof(int));
        table.Columns.Add(nameof(ApiLog.DurationMs), typeof(long));
        table.Columns.Add(nameof(ApiLog.RequestBody), typeof(string));
        table.Columns.Add(nameof(ApiLog.ResponseBody), typeof(string));
        table.Columns.Add(nameof(ApiLog.Exception), typeof(string));
        return table;
    }
}
