# api-logging-channels

Log every API request/response to SQL Server **without making requests wait for the database**.

```
ApiLogFilter ──TryWrite──▶ bounded Channel<ApiLog> ──▶ ApiLogBackgroundService ──SqlBulkCopy──▶ dbo.ApiLogs
(per request, no I/O)                                  (batch by size or time)
```

- **`ApiLogFilter`**: global action filter. Builds an `ApiLog` and calls `TryWrite`, which never blocks.
  If the channel is full (database slow or down), the log is dropped so the API isn't slowed down.
  `[SkipApiLog]` excludes an endpoint.
- **`ApiLogBackgroundService`**: reads the channel and inserts a batch when `BatchSize` logs are collected
  or `FlushInterval` passes, whichever comes first. Writes what's left on shutdown.
- **`SqlApiLogRepository`**: ADO.NET `SqlBulkCopy`, one round trip per batch.

## Run

```bash
cd api-logging-channels && docker compose up -d
```

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Your_strong_Passw0rd' -i /db/create_api_logs.sql
```

```bash
dotnet run
```

Send requests with `api-logging-channels.http`. Tests: `dotnet test api-logging-channels.Tests`.

## Settings (`ApiLogging`)

| Key | Default | |
|---|---|---|
| `Capacity` | `10000` | Max logs held in memory. Logs beyond this are dropped. |
| `BatchSize` | `1000` | Rows per bulk insert |
| `FlushInterval` | `00:00:02` | Max wait before inserting a partial batch |
| `MaxBodyLength` | `4000` | Bodies are truncated to this length |
