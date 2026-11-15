using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Captures the request/response and puts it on the channel. No database work happens here.
///
/// It is both an action filter and a result filter:
///  * action stage: serializes the bound [FromBody] model (the request stream has already been
///    read by model binding) before the action can change it;
///  * result stage: copies the response bytes MVC writes while executing the result. The
///    response object is serialized once, by MVC, instead of a second time just for the log,
///    and the log contains exactly what the client received.
/// </summary>
public sealed class ApiLogFilter(
    ChannelWriter<ApiLog> writer,
    ApiLogStats stats,
    IOptions<ApiLogOptions> options,
    ILogger<ApiLogFilter> logger) : IAsyncActionFilter, IAsyncResultFilter
{
    // Outermost: before [ApiController]'s model validation filter (so automatic 400s are logged)
    // and around every other result filter (so the copied bytes are the final response).
    public const int Order = int.MinValue;

    private static readonly object CaptureKey = new();

    // No ReferenceHandler: tracking references costs a dictionary lookup per object on every
    // request. A cyclic model instead hits MaxDepth, throws, and is logged as a placeholder.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Per-type and per-action answers never change, so compute them once.
    private static readonly ConcurrentDictionary<Type, bool> AsyncEnumerableTypes = new();
    private static readonly ConcurrentDictionary<string, string?> BodyParameterNames = new();

    private readonly long _maxQueueBytes = options.Value.MaxQueueBytes;
    private readonly bool _logAllEndpoints = options.Value.LogAllEndpoints;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!ShouldLog(context.HttpContext))
        {
            await next();
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var requestBody = SerializeRequestBody(context);

        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            // Result filters do not run for an unhandled exception, so log it now.
            TryEnqueue(context.HttpContext, start, requestBody, 500, null, executed.Exception.ToString());
            return;
        }

        context.HttpContext.Items[CaptureKey] = new Capture(start, requestBody);
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Items.Remove(CaptureKey, out var value) || value is not Capture capture)
        {
            await next(); // skipped, or the action stage did not run
            return;
        }

        var tee = ShouldCopyBody(context.Result) ? new TeeStream(http.Response.Body) : null;
        if (tee is not null) http.Response.Body = tee;

        try
        {
            var executed = await next();

            var failed = executed.Exception is not null && !executed.ExceptionHandled;
            TryEnqueue(http, capture.Start, capture.RequestBody,
                statusCode: failed ? 500 : http.Response.StatusCode,
                responseBody: failed ? null : tee?.GetText() ?? Placeholder(context.Result),
                exception: failed ? executed.Exception!.ToString() : null);
        }
        finally
        {
            if (tee is not null)
            {
                http.Response.Body = tee.Inner;
                tee.ReleaseBuffer();
            }
        }
    }

    private void TryEnqueue(HttpContext http, long start, string? requestBody, int statusCode,
        string? responseBody, string? exception)
    {
        try
        {
            var request = http.Request;
            var log = new ApiLog(
                CreatedAt: DateTime.UtcNow,
                TraceId: Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier,
                Method: request.Method,
                Path: request.Path,
                QueryString: request.QueryString.HasValue ? request.QueryString.Value : null,
                StatusCode: statusCode,
                DurationMs: (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                RequestBody: requestBody,
                ResponseBody: responseBody,
                Exception: exception);

            // Never blocks. If the queue is full by count or by memory (database slow/down), the
            // log is dropped: logging must not slow down or break the API, or grow memory without limit.
            if (stats.TryReserve(log.ApproximateBytes, _maxQueueBytes))
            {
                if (writer.TryWrite(log)) return;
                stats.Release(log.ApproximateBytes);
            }
            stats.OnDropped();
        }
        catch (Exception ex)
        {
            // Logging is a side concern: it must never change the outcome of the request.
            logger.LogWarning(ex, "Failed to capture API log");
        }
    }

    // [ApiLog] / [SkipApiLog]: the most specific one wins (action over controller). With neither,
    // LogAllEndpoints decides. Endpoint metadata lookups by type are cached and do not allocate,
    // so endpoints that are not logged cost almost nothing.
    private bool ShouldLog(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<IApiLogMetadata>()?.Enabled ?? _logAllEndpoints;

    private string? SerializeRequestBody(ActionExecutingContext context)
    {
        var name = BodyParameterNames.GetOrAdd(context.ActionDescriptor.Id, static (_, descriptor) =>
        {
            foreach (var p in descriptor.Parameters)
                if (p.BindingInfo?.BindingSource == BindingSource.Body)
                    return p.Name;
            return null;
        }, context.ActionDescriptor);

        if (name is null || !context.ActionArguments.TryGetValue(name, out var value) || value is null)
            return null;

        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), Json);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Could not serialize {Type} for API log", value.GetType());
            return $"<not logged: {value.GetType().Name} could not be serialized>";
        }
    }

    // Copy only bodies MVC builds from a value. Files and streamed sequences can be arbitrarily
    // large or long-lived; buffering them in memory just for a log would be a liability.
    private static bool ShouldCopyBody(IActionResult result) => result switch
    {
        ObjectResult { Value: { } v } => !IsAsyncEnumerable(v.GetType()),
        JsonResult or ContentResult => true,
        _ => false
    };

    private static string? Placeholder(IActionResult result) =>
        result is ObjectResult { Value: { } v } && IsAsyncEnumerable(v.GetType())
            ? $"<not logged: {v.GetType().Name} is streamed>"
            : null;

    private static bool IsAsyncEnumerable(Type type) =>
        AsyncEnumerableTypes.GetOrAdd(type, static t => t.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)));

    private sealed record Capture(long Start, string? RequestBody);
}
