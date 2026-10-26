using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;


using Middleware.Logging.Helpers;
using Middleware.Logging.Models.Logs;


namespace Middleware.Logging.Middleware
{
    public class RequestLoggingMiddleWare
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        public RequestLoggingMiddleWare(RequestDelegate next, ILogger logger)
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

            //create a copy of the response stream before it returns in the pipe. This is because when te response comes back the stream is readonly.
            using (var loggableResponseStream = new MemoryStream())
            {
                var requestTimer = Stopwatch.StartNew();

                var originalResponseStream = context.Response.Body;
                context.Response.Body = loggableResponseStream;
                
                try
                {
                    // get request body
                    var requestBody = await HttpContextHelper.GetRequestBody(context);

                    await _next(context);

                    // reset response response stream to 
                    loggableResponseStream.Seek(0, SeekOrigin.Begin);

                    // read response body and log
                    var reader = new StreamReader(loggableResponseStream);

                    var responseBody = reader.ReadToEnd();

                    AddRequestLogEntry(requestBody, responseBody, context, DateTime.UtcNow,
                        requestTimer.ElapsedMilliseconds);

                    if (context.Response.StatusCode == (int) HttpStatusCode.NoContent)
                    {
                        return;
                    }

                    // reset stream and copy back to original one.
                    loggableResponseStream.Seek(0, SeekOrigin.Begin);

                    await loggableResponseStream.CopyToAsync(originalResponseStream);

                    reader?.Dispose();
                }
                finally
                {
                    //Reassign the original stream. If we are re-throwing an exception this is important as the exception handling middleware will need to write to the response stream.
                    context.Response.Body = originalResponseStream;
                }
            }
        }

        private void AddRequestLogEntry(string requestBody, string responseBody, HttpContext context, DateTime requestTime, Int64 requestDurationInMilliseconds)
        {
            var logEntry = new ApiRequestLog
            {
                ClientId = context.User?.Claims.FirstOrDefault(x => x.Type == "client_id")?.Value,
                CorrelationId = HttpContextHelper.GetCorrelationId(context),
                Endpoint = context.Request.Path,
                Headers = GetRedactedHeaders(context.Request.Headers),
                IdentityId = "Identity id Berkay",
                Method = context.Request.Method,
                Parameters = context.Request.QueryString.ToString(),
                RemoteIp = context.Connection.RemoteIpAddress.ToString(),
                RequestBody = requestBody,
                RequestDuration = requestDurationInMilliseconds,
                RequestTimestamp = requestTime,
                ResponseBody = responseBody,
                ResponseStatus = context.Response.StatusCode,
                UserId = "User Id Berkay"
            };

            _logger.LogInformation("HTTP {Method} {Endpoint} responded {ResponseStatus} in {RequestDuration} ms {@RequestLog}",
                logEntry.Method, logEntry.Endpoint, logEntry.ResponseStatus, logEntry.RequestDuration, logEntry);
        }

        private string GetRedactedHeaders(IHeaderDictionary headers)
        {
            headers["Authorization"] = "*** Redacted ***";
            
            return JsonSerializer.Serialize(headers);
        }
    }
}