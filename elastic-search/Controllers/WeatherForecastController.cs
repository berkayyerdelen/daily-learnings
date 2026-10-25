using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace elastic_search.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly ILogger<WeatherForecastController> _logger;

    public WeatherForecastController(ILogger<WeatherForecastController> logger)
    {
        _logger = logger;
    }

    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    [HttpGet(Name = "GetWeatherForecast")]
    public IEnumerable<WeatherForecast> Get()
    {
        var orderId = 123;
        _logger.LogInformation(
            $"Fetching order {orderId}");
        _logger.LogError(
            new Exception("Failed to fetch order"), $"Failed to fetch order {orderId}");
        throw new Exception($"Failed to fetch order {orderId} global");
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
    }
}


public sealed class RequestLoggingMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const int MaxBodySize = 16 * 1024;

    public async Task InvokeAsync(
        HttpContext context,
        ILogger<RequestLoggingMiddleware> logger)
    {
        var correlationId = GetCorrelationId(context);

        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty(
                   "TraceId", Activity.Current?.TraceId.ToString()))
        {
            var stopwatch = Stopwatch.StartNew();
            var requestBody = await ReadRequestBodyAsync(context.Request);
            var originalResponseBody = context.Response.Body;

            await using var responseBuffer = new MemoryStream();

            var responseBody = "[not captured]";
            var exceptionOccurred = false;

            try
            {
                context.Response.Body = responseBuffer;

                await next(context);

                responseBody = await ReadResponseBodyAsync(
                    context.Response,
                    responseBuffer);
            }
            catch (Exception exception)
            {
                exceptionOccurred = true;

                logger.LogError(
                    exception,
                    "Unhandled exception while processing {RequestMethod} {RequestPath}",
                    context.Request.Method,
                    context.Request.Path);

                throw;
            }
            finally
            {
                stopwatch.Stop();
                context.Response.Body = originalResponseBody;

                if (!exceptionOccurred)
                {
                    responseBuffer.Position = 0;

                    await responseBuffer.CopyToAsync(originalResponseBody);
                }

                logger.LogInformation(
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {DurationMs} ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds);

                logger.LogDebug(
                    "HTTP request and response bodies. RequestBody: {RequestBody}; ResponseBody: {ResponseBody}",
                    requestBody,
                    responseBody);
            }
        }
    }

    private static async Task<string> ReadRequestBodyAsync(
        HttpRequest request)
    {
        if (!IsLoggableContentType(request.ContentType))
            return "[omitted: unsupported content type]";

        if (request.ContentLength > MaxBodySize)
            return "[omitted: body too large]";

        request.EnableBuffering();

        request.Body.Position = 0;

        using var reader = new StreamReader(
            request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        var buffer = new char[MaxBodySize + 1];
        var count = await reader.ReadBlockAsync(
            buffer, 0, buffer.Length);

        request.Body.Position = 0;

        return count > MaxBodySize
            ? "[omitted: body too large]"
            : new string(buffer, 0, count);
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpResponse response,
        MemoryStream buffer)
    {
        if (!IsLoggableContentType(response.ContentType))
            return "[omitted: unsupported content type]";

        if (buffer.Length > MaxBodySize)
            return "[omitted: body too large]";

        buffer.Position = 0;

        using var reader = new StreamReader(
            buffer,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    private static bool IsLoggableContentType(string? contentType) =>
        contentType is not null &&
        (contentType.Contains(
             "application/json", StringComparison.OrdinalIgnoreCase) ||
         contentType.StartsWith(
             "text/", StringComparison.OrdinalIgnoreCase));

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(
                CorrelationIdHeader, out var header) &&
            Guid.TryParse(header.ToString(), out var parsedId))
        {
            return parsedId.ToString();
        }

        return Guid.NewGuid().ToString();
    }
}



public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = context.Response.Headers[
            "X-Correlation-Id"].FirstOrDefault();

        logger.LogError(
            exception,
            "Unhandled exception. TraceId: {TraceId}, " +
            "CorrelationId: {CorrelationId}, Path: {Path}",
            context.TraceIdentifier,
            correlationId,
            context.Request.Path);

        if (context.Response.HasStarted)
        {
            return false;
        }

        context.Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The server could not process your request.",
            Instance = context.Request.Path
        };

        problem.Extensions["correlationId"] = correlationId;

        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(
            problem,
            cancellationToken);

        return true;
    }
}


