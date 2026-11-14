using System.Data;
using Microsoft.Data.SqlClient;

namespace ApiLogging.Api.Logging;

public interface IApiLogRepository
{
    Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken);
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
    private readonly DataTable _table = CreateTable();

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
