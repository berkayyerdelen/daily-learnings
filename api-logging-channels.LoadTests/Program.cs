// Load test for api-logging-channels. Two modes:
//
// http (default): load the running API over HTTP.
//   dotnet run -c Release -- --url http://localhost:5191 --duration 30 --concurrency 200 --body-size 1000
//   dotnet run -c Release -- --path /api/auth/login     (baseline: endpoint is not logged)
//
// db: skip HTTP and push logs straight into the real channel + background service + SqlBulkCopy
// at a target rate. Answers "can the pipeline and my SQL Server sustain N logs/s?".
//   dotnet run -c Release -- --mode db --rps 100000 --duration 30 --body-size 500 --batch-size 1000 --writers 1 \
//       --connection "Server=localhost,1434;Database=api_logging;User Id=sa;Password=...;TrustServerCertificate=True"
//   (omit --connection to measure the pipeline alone, with a no-op repository)
//
// Sends POST /api/orders (or --path) from N concurrent clients for the given duration. Every second it
// prints requests/s, errors and the logging queue stats from GET /api-logs/stats. At the end it
// prints latency percentiles and waits for the queue to drain to show the final DB counts.

using System.Diagnostics;
using ApiLogging.LoadTests;
using System.Net.Http.Json;
using System.Text;

if (Arg("--mode", "http") == "db")
    return await DbLoadTest.RunAsync(Arg);

var url = Arg("--url", "http://localhost:5191").TrimEnd('/');
var duration = TimeSpan.FromSeconds(int.Parse(Arg("--duration", "30")));
var concurrency = int.Parse(Arg("--concurrency", "100"));
var bodySize = int.Parse(Arg("--body-size", "500")); // chars of padding in the request body
// /api/orders is logged; /api/auth/login has [SkipApiLog], so it gives a no-logging baseline.
var path = Arg("--path", "/api/orders");

using var http = new HttpClient(new SocketsHttpHandler
{
    MaxConnectionsPerServer = concurrency,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
})
{ BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };

// Pre-serialized body: the load generator should not be the bottleneck.
// Valid for both /api/orders and /api/auth/login (unknown fields are ignored).
var body = $$"""{"customerId":"c-42","amount":10.5,"cardNumber":"4111","userName":"u","password":"p","notes":"{{new string('x', bodySize)}}"}""";
var payload = Encoding.UTF8.GetBytes(body);

Console.WriteLine($"Target {url}{path}  concurrency {concurrency}  duration {duration.TotalSeconds}s  body {payload.Length} bytes");
var before = await GetStats();
if (before is null)
{
    Console.Error.WriteLine($"Cannot reach {url}/api-logs/stats. Is the API running?");
    return 1;
}
Print("stats before", before);

using var cts = new CancellationTokenSource(duration);
var latencies = new List<double>[concurrency];
long ok = 0, errors = 0;

var workers = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
{
    var mine = latencies[i] = new List<double>(100_000);
    while (!cts.IsCancellationRequested)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new("application/json");
            using var response = await http.PostAsync(path, content, cts.Token);
            if (response.IsSuccessStatusCode) Interlocked.Increment(ref ok);
            else Interlocked.Increment(ref errors);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            break;
        }
        catch
        {
            Interlocked.Increment(ref errors);
        }
        mine.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
})).ToArray();

// Live progress: one line per second.
var started = Stopwatch.StartNew();
long lastOk = 0;
Console.WriteLine();
Console.WriteLine($"{"sec",4} {"req/s",8} {"errors",7} | {"queued",8} {"enqueued",10} {"dropped",9} {"inserted",10} {"failed",8} {"batches",8}");
while (!cts.IsCancellationRequested)
{
    try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { break; }
    var current = Interlocked.Read(ref ok);
    var s = await GetStats();
    Console.WriteLine($"{(int)started.Elapsed.TotalSeconds,4} {current - lastOk,8} {Interlocked.Read(ref errors),7} | " +
                      $"{s?.Queued,8} {s?.Enqueued,10} {s?.Dropped,9} {s?.Inserted,10} {s?.Failed,8} {s?.Batches,8}");
    lastOk = current;
}

await Task.WhenAll(workers);
var elapsed = started.Elapsed.TotalSeconds;

var all = latencies.SelectMany(l => l).Order().ToArray();
Console.WriteLine();
Console.WriteLine($"Requests: {ok:N0} ok, {errors:N0} errors in {elapsed:F1}s  =>  {ok / elapsed:N0} req/s");
if (all.Length > 0)
    Console.WriteLine($"Latency ms: p50 {P(0.50):F2}  p95 {P(0.95):F2}  p99 {P(0.99):F2}  max {all[^1]:F2}");

// Let the background service drain, then show where every log ended up.
Console.WriteLine();
Console.WriteLine("Waiting for the queue to drain...");
var drainUntil = Stopwatch.StartNew();
StatsDto? after;
do
{
    await Task.Delay(500);
    after = await GetStats();
    // QueuedBytes is released only after a batch is inserted (or fails), so 0 means every log
    // has reached the database, including the last partial batch still waiting for FlushInterval.
} while (after is { QueuedBytes: > 0 } && drainUntil.Elapsed < TimeSpan.FromSeconds(60));

Print("stats after", after);
if (before is not null && after is not null)
{
    Console.WriteLine($"This run: enqueued {after.Enqueued - before.Enqueued:N0}, dropped {after.Dropped - before.Dropped:N0}, " +
                      $"inserted {after.Inserted - before.Inserted:N0}, failed {after.Failed - before.Failed:N0}");
}
return 0;

double P(double q) => all[Math.Min(all.Length - 1, (int)(q * all.Length))];

async Task<StatsDto?> GetStats()
{
    try { return await http.GetFromJsonAsync<StatsDto>("/api-logs/stats"); }
    catch { return null; }
}

static void Print(string title, StatsDto? s) => Console.WriteLine(s is null
    ? $"{title}: unavailable"
    : $"{title}: queued {s.Queued:N0}/{s.Capacity:N0}, enqueued {s.Enqueued:N0}, dropped {s.Dropped:N0}, " +
      $"inserted {s.Inserted:N0}, failed {s.Failed:N0}, batches {s.Batches:N0}");

string Arg(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

record StatsDto(long Queued, int Capacity, long QueuedBytes, long Enqueued, long Dropped, long Inserted, long Failed, long Batches);
