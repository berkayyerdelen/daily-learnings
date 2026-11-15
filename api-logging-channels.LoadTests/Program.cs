// Load test for api-logging-channels. Two modes:
//
// http (default): load the running API over HTTP.
//   dotnet run -c Release -- --url http://localhost:5191 --duration 30 --concurrency 200 --body-size 1000
//   dotnet run -c Release -- --path /api/auth/login     (baseline: endpoint is not logged)
//   dotnet run -c Release -- --rps 30000 --concurrency 128   (fixed rate instead of max)
//
// db: skip HTTP and push logs straight into the real channel + background service + SqlBulkCopy
// at a target rate. Answers "can the pipeline and my SQL Server sustain N logs/s?".
//   dotnet run -c Release -- --mode db --rps 100000 --duration 30 --body-size 500 --batch-size 1000 --writers 1 \
//       --connection "Server=localhost,1434;Database=api_logging;User Id=sa;Password=...;TrustServerCertificate=True"
//   (omit --connection to measure the pipeline alone, with a no-op repository)
//
// Sends POST /api/orders (or --path) from N concurrent clients for the given duration, prints
// requests/s and errors every second, and latency percentiles at the end. Dropped logs show up as a
// warning in the API's console; the row count in dbo.ApiLogs shows what was written.

using System.Diagnostics;
using System.Text;
using ApiLogging.LoadTests;

if (Arg("--mode", "http") == "db")
    return await DbLoadTest.RunAsync(Arg);

var url = Arg("--url", "http://localhost:5191").TrimEnd('/');
var duration = TimeSpan.FromSeconds(int.Parse(Arg("--duration", "30")));
var concurrency = int.Parse(Arg("--concurrency", "100"));
// 0 = as fast as possible (finds the maximum). N = a fixed N requests/s spread over the workers,
// to check behaviour at an expected traffic level. Needs enough --concurrency to reach N.
var rps = int.Parse(Arg("--rps", "0"));
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

Console.WriteLine($"Target {url}{path}  concurrency {concurrency}  duration {duration.TotalSeconds}s  body {payload.Length} bytes  " +
                  (rps > 0 ? $"rate {rps:N0} req/s" : "rate: max"));
try
{
    using var probe = await http.GetAsync("/");
}
catch (HttpRequestException)
{
    Console.Error.WriteLine($"Cannot reach {url}. Is the API running?");
    return 1;
}

using var cts = new CancellationTokenSource(duration);
var latencies = new List<double>[concurrency];
long ok = 0, errors = 0, scheduled = 0;
var runClock = Stopwatch.StartNew();

var workers = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
{
    var mine = latencies[i] = new List<double>(100_000);
    while (!cts.IsCancellationRequested)
    {
        if (rps > 0)
        {
            // Fixed rate: request number n is due at n / rps seconds after the start.
            var due = TimeSpan.FromSeconds((double)Interlocked.Increment(ref scheduled) / rps);
            var wait = due - runClock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

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
Console.WriteLine($"{"sec",4} {"req/s",8} {"errors",7}");
while (!cts.IsCancellationRequested)
{
    try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { break; }
    var current = Interlocked.Read(ref ok);
    Console.WriteLine($"{(int)started.Elapsed.TotalSeconds,4} {current - lastOk,8} {Interlocked.Read(ref errors),7}");
    lastOk = current;
}

await Task.WhenAll(workers);
var elapsed = started.Elapsed.TotalSeconds;

var all = latencies.SelectMany(l => l).Order().ToArray();
Console.WriteLine();
Console.WriteLine($"Requests: {ok:N0} ok, {errors:N0} errors in {elapsed:F1}s  =>  {ok / elapsed:N0} req/s");
if (all.Length > 0)
    Console.WriteLine($"Latency ms: p50 {P(0.50):F2}  p95 {P(0.95):F2}  p99 {P(0.99):F2}  max {all[^1]:F2}");

return 0;

double P(double q) => all[Math.Min(all.Length - 1, (int)(q * all.Length))];

string Arg(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
