using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Plain ADO.NET (Npgsql) store.
///
/// Batches are written with PostgreSQL binary COPY rather than INSERT statements:
///  * one round trip and one statement per batch, no SQL text to parse or plan;
///  * no parameter limit (a multi-row INSERT tops out at 65,535 parameters, which is
///    ~4,600 rows for this table);
///  * atomic: if <see cref="NpgsqlBinaryImporter.CompleteAsync"/> is not reached, disposing
///    the importer cancels the COPY and nothing from the batch is committed.
/// The SQL Server equivalent is SqlBulkCopy.
/// </summary>
public sealed class PostgresApiLogStore : IApiLogStore
{
    private const string CopyCommand = """
        COPY api_logs (
            occurred_at, trace_id, http_method, path, query_string, endpoint,
            status_code, duration_ms, request_body, response_body,
            client_ip, user_agent, user_id, exception
        ) FROM STDIN (FORMAT BINARY)
        """;

    // Kept in sync with db/postgres/001_create_api_logs.sql (used by docker compose).
    internal const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS api_logs (
            id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            occurred_at   timestamptz      NOT NULL,
            trace_id      text             NOT NULL,
            http_method   text             NOT NULL,
            path          text             NOT NULL,
            query_string  text             NULL,
            endpoint      text             NULL,
            status_code   integer          NOT NULL,
            duration_ms   double precision NOT NULL,
            request_body  text             NULL,
            response_body text             NULL,
            client_ip     text             NULL,
            user_agent    text             NULL,
            user_id       text             NULL,
            exception     text             NULL
        );

        -- Append-only, time-ordered data: BRIN is tiny and almost free to maintain on insert.
        CREATE INDEX IF NOT EXISTS ix_api_logs_occurred_at ON api_logs USING brin (occurred_at);
        CREATE INDEX IF NOT EXISTS ix_api_logs_trace_id ON api_logs (trace_id);
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly ApiLoggingOptions _options;

    public PostgresApiLogStore(NpgsqlDataSource dataSource, IOptions<ApiLoggingOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(CreateTableSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task WriteBatchAsync(IReadOnlyList<ApiLogEntry> batch, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var importer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);
        importer.Timeout = _options.WriteTimeout;

        foreach (var e in batch)
        {
            await importer.StartRowAsync(cancellationToken);
            // timestamptz requires UTC in Npgsql 6+.
            await importer.WriteAsync(e.OccurredAt.ToUniversalTime(), NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(e.TraceId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(e.HttpMethod, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(e.Path, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(importer, e.QueryString, cancellationToken);
            await WriteNullableAsync(importer, e.Endpoint, cancellationToken);
            await importer.WriteAsync(e.StatusCode, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(e.DurationMs, NpgsqlDbType.Double, cancellationToken);
            await WriteNullableAsync(importer, e.RequestBody, cancellationToken);
            await WriteNullableAsync(importer, e.ResponseBody, cancellationToken);
            await WriteNullableAsync(importer, e.ClientIp, cancellationToken);
            await WriteNullableAsync(importer, e.UserAgent, cancellationToken);
            await WriteNullableAsync(importer, e.UserId, cancellationToken);
            await WriteNullableAsync(importer, e.Exception, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static Task WriteNullableAsync(NpgsqlBinaryImporter importer, string? value, CancellationToken ct) =>
        value is null
            ? importer.WriteNullAsync(ct)
            : importer.WriteAsync(value, NpgsqlDbType.Text, ct);
}
