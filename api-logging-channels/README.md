# api-logging-channels

Log every API request/response to SQL Server **without making requests wait for the database**.

```
ApiLogFilter ──TryWrite──▶ bounded Channel<ApiLog> ──▶ ApiLogBackgroundService ──SqlBulkCopy──▶ dbo.ApiLogs
(per request, no I/O)                                  (batch by size or time)
```

- **`ApiLogFilter`**: global action filter. Builds an `ApiLog` and calls `TryWrite`, which never blocks.
  If the channel is full (database slow or down), the log is dropped so the API isn't slowed down.
  Bodies are stored in full (no truncation).
  `[SkipApiLog]` excludes an endpoint.
- **`ApiLogBackgroundService`**: reads the channel and inserts a batch when `BatchSize` logs are collected
  or `FlushInterval` passes, whichever comes first. Writes what's left on shutdown.
- **`SqlApiLogRepository`**: ADO.NET `SqlBulkCopy`, one round trip per batch.

## Choosing which endpoints are logged

```csharp
[ApiLog]                       // every action in this controller is logged
public class OrdersController : ControllerBase
{
    public IActionResult Create(...) { ... }          // logged

    [SkipApiLog]                                     // overrides the controller
    public IActionResult Login(...) { ... }          // never logged
}

public class HealthController : ControllerBase
{
    public IActionResult Ping() { ... }              // not logged: no attribute

    [ApiLog]
    public IActionResult Deep() { ... }              // logged: opted in on the action
}
```

- Only endpoints with `[ApiLog]` (on the action or its controller) are logged.
- The most specific attribute wins: an action's `[SkipApiLog]` beats its controller's `[ApiLog]`, and the other way round.
- `"ApiLogging": { "LogAllEndpoints": true }` logs every controller endpoint without an attribute; `[SkipApiLog]` still
  excludes endpoints.
- Endpoints that aren't logged cost almost nothing: one cached metadata lookup, no allocation.

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

## Reading logs while they're written (troubleshooting)

`db/troubleshooting.sql` has ready-made queries: latest errors, latest requests to an endpoint, one
request by trace id, a full row by id, slowest requests, and error rate per minute.

- **No database changes needed.** The app only needs the plain table in `db/create_api_logs.sql`.
- **Safe during heavy inserts:** the queries run at `READ UNCOMMITTED`, so they take no row locks and
  neither wait for nor block bulk inserts. They may briefly show rows from a batch still being inserted.
- **Fast without extra indexes:** `Id` only increases, so "latest" queries read the primary key backwards,
  and the rest are limited to a recent `Id` range instead of scanning the whole table.
- **Logs contain full request/response bodies,** which can include personal data or secrets. Restrict who
  can query this table.

## Queue stats: `GET /api-logs/stats`

```json
{ "queued": 12, "capacity": 10000, "queuedBytes": 31560, "maxQueueBytes": 268435456,
  "enqueued": 1000271, "dropped": 0, "inserted": 1000259, "failed": 0, "batches": 1002,
  "gen2SizeAfterGcBytes": 3012345, "gcHeapBytes": 412000000, "workingSetBytes": 812000000, ... }
```

`queued` is how many logs are in memory right now. `dropped` > 0 means the database could not keep up
(or the memory budget was hit). To watch for leaks, use `gen2SizeAfterGcBytes`: it must level off, not climb.

## Load test

```bash
dotnet run -c Release --project api-logging-channels
```

```bash
dotnet run -c Release --project api-logging-channels.LoadTests -- --duration 30 --concurrency 64 --body-size 1000
```

Without `--rps` it sends as fast as possible, which finds the maximum but always ends in drops once
requests outpace SQL Server's insert rate. That's by design: a log is dropped rather than slowing a request.
Use `--rps N --concurrency 128` to test at your expected traffic, where the target is 0 drops. To find
SQL Server's insert ceiling, run `--mode db --rps N` at increasing N; the highest N with 0 drops is it.

It prints req/s, errors and the queue stats every second, then latency percentiles, and waits for the queue
to drain. `--path /api/auth/login` hits an endpoint with `[SkipApiLog]`, as a no-logging baseline.

**Results on a 4-core container** (load generator on the same machine, SQL Server unreachable, so every
batch insert failed: the database-down worst case):

| | req/s | p50 | p99 | memory after ~2.7M requests |
|---|---|---|---|---|
| not logged (baseline) | ~27.7k | 1.6 ms | 11.4 ms | |
| logged, 1 KB bodies | 24–34k | 1.7–1.9 ms | 5.6–12.4 ms | gen2 2.4 → 3.0 → 3.7 MB (stable), working set ~810 MB (stable) |

- Logging overhead was within run-to-run noise. Requests never waited for the (down) database: 0 errors.
- When the writer fell behind, logs were dropped (about 1.5%) instead of being held in memory.
- An in-process check (300k requests, full GC after every 50k) stayed flat at 3.0 MB with every log inserted.
- The ~800 MB working set comes from the server garbage collector reserving memory per core, not from a
  leak. If that matters, enable .NET 8 DATAS (`<GarbageCollectionAdaptationMode>1</GarbageCollectionAdaptationMode>`).
- Not yet measured against a real SQL Server: run it there to see the actual insert rate.

## Filter cost per request (BenchmarkDotNet)

```bash
dotnet run -c Release --project api-logging-channels.Benchmarks
```

Time the filter adds to one request (with logging minus without), measured on this 4-core container:

| Response | Re-serializing the response for the log | Copying the bytes MVC wrote (current) |
|---|---|---|
| List of 50 orders, small bodies | 22.3 µs | **5.7 µs** |
| List of 50 orders, 10K-char bodies | 44.4 µs | **15.0 µs** |
| Single object, 100-char body | 2.8 µs | 3.1 µs |
| Single object, 10K-char body | 11.9 µs | 11.1 µs |

The filter copies the response bytes while MVC writes them, so the response object is serialized
once, not twice. The win grows with how expensive a response is to serialize. Allocations are about
the same either way: they're dominated by the request/response text that is stored in full.

## Memory and performance notes

- **Memory is bounded twice:** by count (`Capacity`) and by bytes (`MaxQueueBytes`). Bodies are not
  truncated, so a count limit alone does not bound memory. Bytes are released after a batch is inserted,
  so the budget also covers the batch in flight.
- **Logging never breaks a request.** A body that cannot be serialized is logged as a placeholder.
  `IQueryable` / `IAsyncEnumerable` results are not serialized, because doing so would run the query again.
- **Bodies over ~42K characters (85 KB)** land on the Large Object Heap. Use `[SkipApiLog]` on endpoints that
  return very large payloads.
- The `DataTable` used by `SqlBulkCopy` is created once and reused across batches.
- The logged response is exactly what the client received, including `ProblemDetails`, plain-text
  results and the final status code. Files and `IAsyncEnumerable` streams are not copied, so they're never
  buffered in memory just for a log.
- Per-request lookups that never change (body parameter per action, `IAsyncEnumerable` check per type) are
  cached. `[SkipApiLog]` is checked through ASP.NET Core's cached endpoint metadata. The filter updates one
  shared counter per request (the memory budget); `enqueued` is derived.
- **Retries only for temporary errors:** timeouts, lost connections, deadlock victim (1205), lock timeout,
  and Azure SQL failover/throttling codes. Permanent errors (schema, permissions, a missing database) are
  not retried, so they don't hold up newer batches. Each batch is one transaction, so a retry after a
  deadlock or a lost connection can't create duplicates. The exception is a timeout right at commit time,
  which in rare cases can write a batch twice.
- **One writer by default.** All writers insert at the end of the same table, and a load test showed a
  second writer waiting for the first one's locks (`LCK_M_IX`). `WriterCount` can be raised if a load test
  against your SQL Server (`--mode db --writers N`) shows a higher insert rate. Rows are streamed through
  `ApiLogDataReader` (no `DataTable` copy, no shared state), so raising it is safe.
- **No lock escalation, without database settings:** SQL Server swaps a statement's row locks for a table
  lock at about 5,000 locks, which would block other API instances and queries. `BatchSize` is capped at
  4,000 (default 1,000) and validated at startup, so a batch never reaches that point.
- **Short transactions:** each batch is one quick bulk insert. `SqlBulkCopy` streaming is off: under HTTP
  load it made a batch take 44 s while holding its locks.
- **Deadlocks:** our inserts only append to the end of an `IDENTITY` clustered index, so they don't deadlock
  with each other. They become possible if something else uses the table (a retention job deleting in
  large chunks, reports at `REPEATABLE READ`/`SERIALIZABLE`, extra indexes or triggers), which is why 1205
  is retried.

## Settings (`ApiLogging`)

| Key | Default | |
|---|---|---|
| `LogAllEndpoints` | `false` | `false`: only `[ApiLog]` endpoints. `true`: all, except `[SkipApiLog]` |
| `Capacity` | `10000` | Max logs held in memory |
| `MaxQueueBytes` | `268435456` (256 MB) | Max memory for queued and in-flight logs |
| `WriterCount` | `1` | Parallel writer loops, each with its own connection (1–16) |
| `BatchSize` | `1000` | Rows per bulk insert (max 4,000, to stay under lock escalation) |
| `FlushInterval` | `00:00:02` | Max wait before inserting a partial batch |
| `MaxRetries` | `3` | Extra attempts for a batch that failed with a temporary error |
| `RetryBaseDelay` | `00:00:00.200` | First retry delay; doubles each attempt with random jitter, capped at 5 s |
