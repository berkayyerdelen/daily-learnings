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

`db/troubleshooting.sql` has ready-made queries: recent errors, a time window, one request by trace id,
a full row by id, slowest requests, and error rate per minute.

- **Safe during heavy inserts:** the database uses `READ_COMMITTED_SNAPSHOT`, so queries read the last
  committed rows and neither wait for nor block bulk inserts.
- **Fast:** each query matches an index (`CreatedAt`, `TraceId`, and a filtered index on
  `StatusCode >= 400`). Keep a time range on list queries; bodies are fetched by `Id` once the row is found.
- **Insert cost:** every index slows inserts. `IX_ApiLogs_TraceId` (random values) is the most expensive;
  drop it if you never search by trace id.
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
- **Parallel writers** (`WriterCount`) read the same channel and bulk-insert on separate connections,
  so throughput isn't capped by one connection, and a batch that is retrying doesn't hold up the others.
  Rows are streamed through `ApiLogDataReader` (no `DataTable` copy, no shared state). With no database,
  4 writers kept up with 200,000 logs/s. Measure your SQL Server with `--mode db --writers N`.
- **Parallel writers don't deadlock each other:** they only insert new rows, and the table has no foreign
  keys, unique secondary indexes or triggers. What *can* happen is lock escalation to a table lock, which
  makes writers take turns, so the create script sets `LOCK_ESCALATION = DISABLE` (delete old rows in
  chunks because of that) and `OPTIMIZE_FOR_SEQUENTIAL_KEY = ON` for the shared last page.
- **Deadlocks:** our inserts only append to the end of an `IDENTITY` clustered index, so they don't deadlock
  with each other. They become possible if something else uses the table (a retention job deleting in
  large chunks, reports at `REPEATABLE READ`/`SERIALIZABLE`, extra indexes or triggers), which is why 1205
  is retried.

## Settings (`ApiLogging`)

| Key | Default | |
|---|---|---|
| `Capacity` | `10000` | Max logs held in memory |
| `MaxQueueBytes` | `268435456` (256 MB) | Max memory for queued and in-flight logs |
| `WriterCount` | `2` | Parallel writer loops, each with its own connection |
| `BatchSize` | `1000` | Rows per bulk insert |
| `FlushInterval` | `00:00:02` | Max wait before inserting a partial batch |
| `MaxRetries` | `3` | Extra attempts for a batch that failed with a temporary error |
| `RetryBaseDelay` | `00:00:00.200` | First retry delay; doubles each attempt with random jitter, capped at 5 s |
