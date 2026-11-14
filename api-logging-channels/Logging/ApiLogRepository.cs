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
/// </summary>
public sealed class SqlApiLogRepository(string connectionString) : IApiLogRepository
{
    public async Task InsertBatchAsync(IReadOnlyList<ApiLog> logs, CancellationToken cancellationToken)
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

        foreach (var l in logs)
            table.Rows.Add(l.CreatedAt, l.TraceId, l.Method, l.Path, l.QueryString, l.StatusCode,
                l.DurationMs, l.RequestBody, l.ResponseBody, l.Exception);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = "dbo.ApiLogs" };
        foreach (DataColumn column in table.Columns)
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName); // Id is identity, skipped

        await bulkCopy.WriteToServerAsync(table, cancellationToken);
    }
}
