using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Plain ADO.NET (Microsoft.Data.SqlClient) store that writes each batch with
/// <see cref="SqlBulkCopy"/>, the TDS bulk-load protocol behind BCP / BULK INSERT.
///
/// Why SqlBulkCopy and not the other ADO.NET options:
///  * multi-row INSERT ... VALUES: capped at 1,000 rows and 2,100 parameters per statement
///    (~150 rows for this 14-column table), and every batch size is a new plan to compile;
///  * table-valued parameters: fine for a few thousand rows, but need a user-defined table
///    type and still run through the regular insert path;
///  * SqlDataAdapter.UpdateBatchSize / DbBatch: fewer round trips, but still one INSERT per row.
/// Bulk copy streams rows as a binary row set and is the fastest path into SQL Server.
///
/// Rows are streamed through <see cref="ApiLogDataReader"/> instead of a DataTable, which
/// would copy and box every value of every row again.
/// </summary>
public sealed class SqlServerApiLogStore : IApiLogStore
{
    private const string TableName = "dbo.ApiLogs";

    internal const string CreateTableSql = """
        IF OBJECT_ID(N'dbo.ApiLogs', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ApiLogs (
                Id           bigint IDENTITY(1,1) NOT NULL,
                OccurredAt   datetimeoffset(7) NOT NULL,
                TraceId      varchar(64)       NOT NULL,
                HttpMethod   varchar(16)       NOT NULL,
                Path         nvarchar(2048)    NOT NULL,
                QueryString  nvarchar(max)     NULL,
                Endpoint     nvarchar(256)     NULL,
                StatusCode   int               NOT NULL,
                DurationMs   float             NOT NULL,
                RequestBody  nvarchar(max)     NULL,
                ResponseBody nvarchar(max)     NULL,
                ClientIp     varchar(45)       NULL,
                UserAgent    nvarchar(512)     NULL,
                UserId       nvarchar(256)     NULL,
                Exception    nvarchar(max)     NULL,
                -- Ever-increasing key + many concurrent writers = last-page latch contention.
                -- OPTIMIZE_FOR_SEQUENTIAL_KEY (SQL Server 2019+) mitigates it.
                CONSTRAINT PK_ApiLogs PRIMARY KEY CLUSTERED (Id) WITH (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON)
            );
            CREATE INDEX IX_ApiLogs_OccurredAt ON dbo.ApiLogs (OccurredAt);
            CREATE INDEX IX_ApiLogs_TraceId ON dbo.ApiLogs (TraceId);
        END
        """;

    private readonly string _connectionString;
    private readonly ApiLoggingOptions _options;

    public SqlServerApiLogStore(string connectionString, IOptions<ApiLoggingOptions> options)
    {
        _connectionString = connectionString;
        _options = options.Value;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(CreateTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task WriteBatchAsync(IReadOnlyList<ApiLogEntry> batch, CancellationToken cancellationToken)
    {
        // Connection pooling is on by default; Open/Dispose just rents and returns.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Deliberately NOT SqlBulkCopyOptions.TableLock: on a table with a clustered index it
        // takes an exclusive lock, which serializes bulk loads from every app instance.
        // FireTriggers and CheckConstraints are off by default, which is what we want here.
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, externalTransaction: null)
        {
            DestinationTableName = TableName,
            BatchSize = 0, // whole batch in one transaction: all rows or none
            BulkCopyTimeout = (int)_options.WriteTimeout.TotalSeconds,
            EnableStreaming = true
        };

        // Map by name so column order in the table does not matter (Id is skipped: identity).
        foreach (var column in ApiLogDataReader.Columns)
            bulk.ColumnMappings.Add(column, column);

        using var reader = new ApiLogDataReader(batch);
        await bulk.WriteToServerAsync(reader, cancellationToken);
    }
}
