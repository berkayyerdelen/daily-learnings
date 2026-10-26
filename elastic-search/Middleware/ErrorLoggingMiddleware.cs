using System.Diagnostics;
using System.Text.Json;
using Middleware.Logging.Helpers;
using Middleware.Logging.Models;
using Middleware.Logging.Models.Logs;

namespace Middleware.Logging.Middleware
{
    public class ErrorLoggingMiddleWare
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        public ErrorLoggingMiddleWare(RequestDelegate next, ILogger logger)
        {
            _next = next;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Method.Equals("options", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);

                return;
            }
            
            var requestTimer = Stopwatch.StartNew();
            var requestBody = string.Empty;
            try
            {
                requestBody = await HttpContextHelper.GetRequestBody(context);
                await _next(context);
            }
            catch (Exception ex)
            {
                var errorLog = BuildErrorLog(context, requestBody, requestTimer);
                _logger.LogError(ex, "HTTP {Method} {Endpoint} failed with {ResponseStatus} {@ErrorLog}",
                    errorLog.Method, errorLog.Endpoint, errorLog.ResponseStatus, errorLog);
                await ReturnErrorResponse(context, new Dictionary<string, string>{{"UNHANDLED_EXCEPTION", "An unexpected error occurred"}}, StatusCodes.Status500InternalServerError);
            }
        }

        private static ApiErrorLog BuildErrorLog(HttpContext context, string requestBody, Stopwatch requestTimer, int statusCode = StatusCodes.Status500InternalServerError)
        {
            return new ApiErrorLog
            {
                ClientId = context.User?.Claims.FirstOrDefault(x => x.Type == "client_id")?.Value,
                CorrelationId = HttpContextHelper.GetCorrelationId(context),
                Endpoint = context.Request.Path,
                IdentityId = "Identity id Berkay",
                Method = context.Request.Method,
                RemoteIp = context.Connection?.RemoteIpAddress.ToString(),
                RequestBody = requestBody,
                RequestDuration = requestTimer.ElapsedMilliseconds,
                ResponseStatus = statusCode,
                UserId = "User Id Berkay"
            };
        }
        
        private async Task ReturnErrorResponse(HttpContext context, Dictionary<string, string> errorDictionary, int statusCode)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning($"The response has already started, the {nameof(ErrorLoggingMiddleWare)} will not be executed.");

                throw new InvalidOperationException("The response has already started");
            }
            
            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var error = BuildErrorResponse(errorDictionary);
            
            await context.Response.WriteAsync(JsonSerializer.Serialize(error,new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
        
        private static ErrorResponseModel BuildErrorResponse(IDictionary<string, string> errorDictionary)
        {
            return new ErrorResponseModel
            {
                Errors = errorDictionary.Select(c => new ErrorModel { ErrorCode = c.Key, ErrorMessage = c.Value })
            };
        }
    }
}
