# api-logging-channels

Log every API request/response to a database table **without making requests wait for the database**.

A global MVC action filter captures each call into an `ApiLogEntry` and drops it on a bounded in-memory
`System.Threading.Channels` queue. One background writer (or a few) drains the queue and bulk-inserts batches with
plain ADO.NET. SQL Server (`SqlBulkCopy`) is the default store; PostgreSQL (binary `COPY`) is the alternative.

```
request thread                       background
──────────────                       ──────────
ApiLogActionFilter                   ApiLogBatchWriter (x WriterConcurrency)
  capture entry  ──TryWrite──▶ [ bounded Channel ] ──▶ batch (size OR interval) ──▶ IApiLogStore
  (no I/O, never blocks)        drop + count if full     retry w/ backoff, then drop    SqlBulkCopy / COPY
```

## Run it

```bash
cd api-logging-channels && docker compose up -d
```

```bash
dotnet run --project api-logging-channels
```

Use `api-logging-channels.http` to send requests. In Development the table is created on startup
(`ApiLogging:EnsureSchema`). Otherwise run `db/sqlserver/001_create_api_logs.sql`.

```bash
dotnet test api-logging-channels.Tests
```

## Design decisions

| Decision | Why |
|---|---|
| **Bounded channel, `TryWrite` only** | Logging must never add latency or cause an outage. When the DB is slow or down, the queue fills and new entries are **dropped and counted** (`api_logs.dropped`), not awaited. Memory is capped at `ChannelCapacity × entry size`. |
| **`FullMode.Wait` + `TryWrite`, not `DropWrite`** | With `DropWrite`, `TryWrite` returns `true` and silently discards the item, so you can't count drops. With `Wait`, `TryWrite` returns `false` when the queue is full and never blocks. |
| **Flush on size *or* interval** | `BatchSize` gives throughput under load. `FlushInterval` gives bounded latency when traffic is low. The timer covers the whole batch, not each entry. |
| **Bulk protocol, not INSERTs** | One round trip and one atomic write per batch. See below. |
| **Retry with jittered backoff, then drop the batch** | A short DB blip loses nothing. A long outage doesn't stall the pipeline forever. `api_logs.failed` makes the loss visible. |
| **`IHostedLifecycleService`, drain in `StoppedAsync`** | Hosted services stop in reverse order, so a `BackgroundService` would close the queue *while Kestrel is still finishing requests*. `StoppedAsync` runs after the server has drained, so the last requests are logged too. The drain is limited by `HostOptions.ShutdownTimeout`. Verified: a 1-minute flush interval, SIGTERM, and every row was written. |
| **Log bound models, not raw streams** | In an action filter, the request stream has already been read and the response hasn't been written yet. Logging the bound argument and the `ObjectResult` value needs no buffering or stream swapping, and lets you mask fields with `[SensitiveData]`. Trade-off: when the body fails to bind, the request body is logged as `null`. The 400 response and its errors are still logged. |
| **`Order = int.MinValue`** | The filter wraps `[ApiController]`'s automatic model-validation filter, so automatic 400s are logged. |
| **`[SkipApiLog]`, `[SensitiveData]`, truncation** | Credentials never reach the table. Large payloads can't blow up row size. |
| **Metrics (`ApiLogging` meter)** | `enqueued`, `dropped`, `written`, `failed`, `queue_depth`, `batch_size`, `flush_duration`. A fire-and-forget pipeline needs these, or it fails silently. |

**Delivery guarantee: at-most-once.** If the process crashes, the entries still in memory are lost (up to `ChannelCapacity`).
That is fine for diagnostics. It is **not** fine for audit or compliance logs: for those, see "When not to use this" below.

## Does ADO.NET do batch inserts? (SQL Server)

Yes. These are the options, from slowest to fastest:

| Option | Limits / notes |
|---|---|
| One `INSERT` per row, in a loop | One round trip per row. Never use this for logs. |
| `DbBatch` / `SqlDataAdapter.UpdateBatchSize` | Fewer round trips, but still one INSERT statement per row on the server. |
| Multi-row `INSERT ... VALUES (...),(...)` | Max **1,000 rows** and **2,100 parameters** per statement, which is about 150 rows for 14 columns. Each distinct batch size compiles a new plan. |
| Table-valued parameter + `INSERT ... SELECT` | Good for up to a few thousand rows. Needs a user-defined table type. |
| **`SqlBulkCopy`** ✅ | Uses the TDS bulk-load protocol (the same one BCP uses). No row or parameter limits. Can use minimal logging. This is what the project uses. |

Implementation notes (`SqlServerApiLogStore`):
- **Streams rows through a custom `DbDataReader` (`ApiLogDataReader`)** instead of a `DataTable`. A `DataTable` would copy and box every value of every row a second time, which is real GC pressure at high volume.
- `BatchSize = 0`: the whole batch is one transaction, so it is either fully written or not written at all. That makes retries safe from partial duplicates.
- **No `SqlBulkCopyOptions.TableLock`.** On a table with a clustered index it takes an exclusive lock, so bulk loads from N app instances would run one at a time. It only helps on a heap or columnstore, where bulk-update (BU) locks are compatible with each other.
- Columns are mapped by name. `Id` is an identity column and is skipped.

## At 100k requests per second

This code handles the in-process part. At that rate, **the database is the problem, not the channel.**

**1. Do the math first.** Example: 100k req/s with a 2 KB average row = **~200 MB/s ≈ 17 TB/day**. `nvarchar` doubles
the size of text. No single SQL Server's transaction log will sustain that with full bodies on every request. These
are estimates; measure your own payload sizes.

**2. Reduce the volume. This is the biggest lever.**
- Always log metadata (~200 bytes/row). Log bodies for a sample only:
  `BodySampleRate: 0.01` plus `AlwaysLogBodiesOnError: true` (both implemented).
  Unsampled requests skip JSON serialization entirely, which also removes the main **CPU** cost from the request thread.
- Keep `MaxBodyLength` small. Consider `varchar` with a UTF-8 collation (SQL Server 2019+) instead of `nvarchar`.

**3. Shape the table for append-only writes.**
- **Clustered columnstore index** (often 5 to 10 times smaller), with **daily partitions**. Retention then becomes
  `TRUNCATE TABLE ... WITH (PARTITIONS (...))` or a partition `SWITCH` instead of a huge `DELETE`.
- Bulk loads of **≥ 102,400 rows** go straight into compressed rowgroups and skip the delta store. Smaller batches
  still work: the tuple mover compresses them later.
- If you stay on rowstore: an identity clustered key with many concurrent writers causes last-page latch contention.
  `OPTIMIZE_FOR_SEQUENTIAL_KEY = ON` (already in the DDL) helps. Every extra index slows inserts.
- Put logs in their **own database**, so log I/O and recovery model don't affect your OLTP database.

**4. Size each instance.** 100k req/s is fleet-wide. With 20 instances, each handles about 5k/s.
- `BatchSize` 2,000 to 10,000 for `SqlBulkCopy`. Raise `WriterConcurrency` only when `queue_depth` keeps growing
  while the database still has headroom. Each writer uses one more connection.
- `ChannelCapacity` = rate × the outage you want to ride out. Memory = capacity × entry size: 50,000 × 4 KB ≈ 200 MB.
  The channel buffers **seconds**, not minutes. Beyond that, entries are dropped by design.

**5. When not to use this (in-memory queue → SQL).**
- If you can't lose logs (audit, billing, compliance), use a durable buffer: write to Kafka or Event Hubs (or a local
  disk spool). A separate consumer then bulk-loads at its own pace and can replay.
- At this volume, a store built for logs (ClickHouse, Azure Data Explorer, Elasticsearch/OpenSearch) or an
  OpenTelemetry logs pipeline is usually cheaper and much easier to query than a relational table. In that setup,
  SQL Server keeps only what you need relationally, such as errors or business audit events.

The `IApiLogStore` seam is how you move between these options without touching the filter or the batching code.

## Configuration (`ApiLogging` section)

| Key | Default | |
|---|---|---|
| `Provider` | `SqlServer` | `SqlServer` or `Postgres`. The connection string is `ConnectionStrings:ApiLogs`. |
| `Enabled` | `true` | Kill switch |
| `ChannelCapacity` | `10000` | Memory bound. Entries beyond this are dropped. |
| `BatchSize` | `500` | Flush when this many entries are collected |
| `FlushInterval` | `00:00:02` | Flush a partial batch after this long |
| `WriterConcurrency` | `1` | Number of parallel bulk writers |
| `MaxRetries` | `3` | Retries per batch before it is dropped |
| `BodySampleRate` | `1.0` | Fraction of successful requests whose bodies are logged |
| `AlwaysLogBodiesOnError` | `true` | Always log bodies for 4xx/5xx |
| `MaxBodyLength` | `8192` | Truncation limit for bodies and exception text |
| `WriteTimeout` | `00:00:30` | Timeout for one batch write |
| `EnsureSchema` | `false` (`true` in Development) | Create the table on startup |
