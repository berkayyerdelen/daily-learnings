using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace ApiLogging.Api.Logging;

/// <summary>
/// Global MVC action filter that captures each request/response and hands it to
/// <see cref="ApiLogQueue"/>. It does no I/O: the request pays for building one record
/// and one non-blocking channel write.
///
/// Why log the *bound models* instead of the raw HTTP bodies:
///  * by the time an action filter runs, model binding has already consumed the request
///    stream, so the raw body is only readable if buffering is enabled for every request;
///  * the response body is not written until after action filters complete, so capturing
///    raw bytes would mean swapping Response.Body for every request;
///  * bound models let us mask fields with [SensitiveData].
/// The trade-off: a request whose body fails to bind is logged with a null request body
/// (the 400 response and its validation errors are still logged).
/// </summary>
public sealed class ApiLogActionFilter : IAsyncActionFilter
{
    /// <summary>
    /// Run before every other action filter, including [ApiController]'s automatic
    /// model-validation filter (order -2000), so its 400 responses are logged too.
    /// </summary>
    public const int FilterOrder = int.MinValue;

    private readonly ApiLogQueue _queue;
    private readonly ApiLoggingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ApiLogActionFilter> _logger;

    public ApiLogActionFilter(
        ApiLogQueue queue,
        IOptions<ApiLoggingOptions> options,
        TimeProvider time,
        ILogger<ApiLogActionFilter> logger)
    {
        _queue = queue;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_options.Enabled || IsSkipped(context))
        {
            await next();
            return;
        }

        var occurredAt = _time.GetUtcNow();
        var started = _time.GetTimestamp();

        var sampled = _options.BodySampleRate >= 1 || Random.Shared.NextDouble() < _options.BodySampleRate;
        var bodyArgument = FindBodyArgument(context);
        // Serialize before the action runs when we already know we want it: the action may
        // mutate its arguments. Unsampled requests skip serialization entirely (hot path).
        var requestBody = sampled ? SafeCapture(() => Serialize(bodyArgument)) : null;

        var executed = await next();

        try
        {
            var entry = BuildEntry(context.HttpContext, context, executed, occurredAt, started, requestBody, sampled);

            if (!sampled && _options.AlwaysLogBodiesOnError && entry.StatusCode >= 400)
            {
                // Late capture for errors. The action may have mutated the argument; for a
                // failed request that is an acceptable trade-off against serializing every body.
                entry = entry with
                {
                    RequestBody = SafeCapture(() => Serialize(bodyArgument)),
                    ResponseBody = entry.Exception is null ? SafeCapture(() => CaptureResponseBody(executed.Result)) : null
                };
            }

            _queue.TryEnqueue(entry);
        }
        catch (Exception ex)
        {
            // Logging is a side concern and must never change the outcome of a request.
            _logger.LogWarning(ex, "Failed to capture API log entry");
        }
    }

    private ApiLogEntry BuildEntry(
        HttpContext http,
        ActionExecutingContext executing,
        ActionExecutedContext executed,
        DateTimeOffset occurredAt,
        long started,
        string? requestBody,
        bool includeResponseBody)
    {
        var request = http.Request;
        var unhandledException = executed.Exception is not null && !executed.ExceptionHandled
            ? executed.Exception
            : null;

        return new ApiLogEntry
        {
            OccurredAt = occurredAt,
            TraceId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier,
            HttpMethod = request.Method,
            Path = request.Path.Value ?? "/",
            QueryString = request.QueryString.HasValue ? request.QueryString.Value : null,
            Endpoint = executing.ActionDescriptor is ControllerActionDescriptor cad
                ? $"{cad.ControllerName}.{cad.ActionName}"
                : executing.ActionDescriptor.DisplayName,
            StatusCode = ResolveStatusCode(http, executed, unhandledException),
            DurationMs = _time.GetElapsedTime(started).TotalMilliseconds,
            RequestBody = requestBody,
            ResponseBody = includeResponseBody && unhandledException is null
                ? SafeCapture(() => CaptureResponseBody(executed.Result))
                : null,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = LogBodySerializer.Truncate(request.Headers.UserAgent.ToString(), 512),
            UserId = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null,
            Exception = LogBodySerializer.Truncate(unhandledException?.ToString(), _options.MaxBodyLength)
        };
    }

    private static bool IsSkipped(ActionExecutingContext context) =>
        context.ActionDescriptor.EndpointMetadata.Any(m => m is SkipApiLogAttribute);

    private static object? FindBodyArgument(ActionExecutingContext context)
    {
        // MVC allows a single [FromBody] parameter. [ApiController] infers it for complex types.
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            if (parameter.BindingInfo?.BindingSource == BindingSource.Body &&
                context.ActionArguments.TryGetValue(parameter.Name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private string? Serialize(object? value) => LogBodySerializer.Serialize(value, _options.MaxBodyLength);

    private string? CaptureResponseBody(IActionResult? result) => result switch
    {
        ObjectResult o => LogBodySerializer.Serialize(o.Value, _options.MaxBodyLength),
        JsonResult j => LogBodySerializer.Serialize(j.Value, _options.MaxBodyLength),
        ContentResult c => LogBodySerializer.Truncate(c.Content, _options.MaxBodyLength),
        _ => null // files, streams, redirects, empty results: nothing useful to store
    };

    private static int ResolveStatusCode(HttpContext http, ActionExecutedContext executed, Exception? unhandled)
    {
        if (unhandled is not null)
            return StatusCodes.Status500InternalServerError;

        // The response has not been written yet, so the result is the source of truth.
        return executed.Result is IStatusCodeActionResult { StatusCode: { } code }
            ? code
            : http.Response.StatusCode; // e.g. `return model;` -> ObjectResult with no explicit code -> 200
    }

    private string? SafeCapture(Func<string?> capture)
    {
        try
        {
            return capture();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not capture body for API log");
            return null;
        }
    }
}
