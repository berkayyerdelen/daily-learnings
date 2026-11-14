using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Captures the request/response and puts it on the channel. No database work happens here:
/// the cost per request is building one record and a non-blocking TryWrite.
/// </summary>
public sealed class ApiLogFilter(
    ChannelWriter<ApiLog> writer,
    ApiLogStats stats,
    IOptions<ApiLogOptions> options,
    ILogger<ApiLogFilter> logger) : IAsyncActionFilter
{
    // Run before [ApiController]'s model validation filter so automatic 400s are logged too.
    public const int Order = int.MinValue;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles // a cyclic model must not fail the request
    };

    private readonly long _maxQueueBytes = options.Value.MaxQueueBytes;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is SkipApiLogAttribute))
        {
            await next();
            return;
        }

        var start = Stopwatch.GetTimestamp();
        // The request stream is already read by model binding, so log the bound [FromBody] model.
        // Serialize now: the action may change the object.
        var requestBody = Serialize(GetBodyArgument(context));

        var executed = await next();

        try
        {
            Enqueue(context, executed, start, requestBody);
        }
        catch (Exception ex)
        {
            // Logging is a side concern: it must never change the outcome of the request.
            logger.LogWarning(ex, "Failed to capture API log");
        }
    }

    private void Enqueue(ActionExecutingContext context, ActionExecutedContext executed, long start, string? requestBody)
    {
        var failed = executed.Exception is not null && !executed.ExceptionHandled;
        var request = context.HttpContext.Request;

        var log = new ApiLog(
            CreatedAt: DateTime.UtcNow,
            TraceId: Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier,
            Method: request.Method,
            Path: request.Path,
            QueryString: request.QueryString.HasValue ? request.QueryString.Value : null,
            StatusCode: failed ? 500 : GetStatusCode(executed),
            DurationMs: (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            RequestBody: requestBody,
            ResponseBody: failed ? null : Serialize((executed.Result as ObjectResult)?.Value),
            Exception: failed ? executed.Exception!.ToString() : null);

        // Never blocks. If the queue is full by count or by memory (database slow/down), the log
        // is dropped: logging must not slow down or break the API, or grow memory without limit.
        if (stats.TryReserve(log.ApproximateBytes, _maxQueueBytes))
        {
            if (writer.TryWrite(log))
            {
                stats.OnEnqueued();
                return;
            }
            stats.Release(log.ApproximateBytes);
        }
        stats.OnDropped();
    }

    private static object? GetBodyArgument(ActionExecutingContext context)
    {
        foreach (var p in context.ActionDescriptor.Parameters)
            if (p.BindingInfo?.BindingSource == BindingSource.Body &&
                context.ActionArguments.TryGetValue(p.Name, out var value))
                return value;
        return null;
    }

    private static int GetStatusCode(ActionExecutedContext executed) =>
        executed.Result is IStatusCodeActionResult { StatusCode: { } code }
            ? code
            : executed.HttpContext.Response.StatusCode;

    private string? Serialize(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            // Lazy sequences: serializing would run the query (e.g. EF) a second time, or block
            // on an async stream. MVC streams these to the client; we log a placeholder.
            case IQueryable:
            case not null when IsAsyncEnumerable(value.GetType()):
                return $"<not logged: {value.GetType().Name} is not materialized>";
        }

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

    private static bool IsAsyncEnumerable(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
}
