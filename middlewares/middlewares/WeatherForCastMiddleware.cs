namespace middlewares.middlewares;

public class WeatherForCastMiddleware(RequestDelegate next, RequestCounter requestCounter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var currentCounter = requestCounter.Increment();

        var value = context.Request.Headers["x-key"];

        if (string.IsNullOrWhiteSpace(value))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.TryAdd("x-key", currentCounter.ToString());
            await context.Response.WriteAsync(
                $"Missing x-key header, request count: {currentCounter}");

            return;
        }

        await next(context);
    }
}