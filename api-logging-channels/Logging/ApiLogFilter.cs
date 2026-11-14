using System.Diagnostics;
using System.Text.Json;
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
public sealed class ApiLogFilter(ChannelWriter<ApiLog> writer, IOptions<ApiLogOptions> options) : IAsyncActionFilter
{
    // Run before [ApiController]'s model validation filter so automatic 400s are logged too.
    public const int Order = int.MinValue;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly int _maxBodyLength = options.Value.MaxBodyLength;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is SkipApiLogAttribute))
        {
            await next();
            return;
        }

        var start = Stopwatch.GetTimestamp();
        // The request stream is already read by model binding, so log the bound [FromBody] model.
        var requestBody = Serialize(GetBodyArgument(context));

        var executed = await next();

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
            Exception: failed ? Truncate(executed.Exception!.ToString()) : null);

        // Never blocks. If the queue is full (database slow/down) the log is dropped:
        // logging must not slow down or break the API.
        writer.TryWrite(log);
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

    private string? Serialize(object? value) =>
        value is null ? null : Truncate(JsonSerializer.Serialize(value, value.GetType(), Json));

    private string Truncate(string value) =>
        value.Length <= _maxBodyLength ? value : value[.._maxBodyLength];
}
