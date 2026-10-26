using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Middleware.Logging.Helpers
{
    public static class HttpContextHelper
    {
        private static readonly string[] ContentTypesToLog =
        {
            "application/json", 
            "application/xml", 
            "text/xml",
            "text/json",
            "text/javascript",
            "application/x-www-form-urlencoded",
            "application/ld+json",
            "multipart/form-data",
            "text/plain"
        }; 
        
        internal static async Task<string> GetRequestBody(HttpContext context)
        {
            if (context.Request.ContentType == null || !ContentTypesToLog.Any(x => context.Request.ContentType.ToLower().Contains(x)))
            {
                return String.Empty;
            }
            
            using (var requestBodyStream = new MemoryStream())
            {
                context.Request.EnableBuffering();
                await context.Request.Body.CopyToAsync(requestBodyStream);

                requestBodyStream.Seek(0, SeekOrigin.Begin);
                context.Request.Body.Seek(0, SeekOrigin.Begin);

                using (var streamReader = new StreamReader(requestBodyStream))
                {
                    return await streamReader.ReadToEndAsync();
                }
            }
        }

        internal static Guid? GetCorrelationId(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("x-correlation-id", out StringValues correlationId))
            {
                if (!StringValues.IsNullOrEmpty(correlationId) && Guid.TryParse(correlationId.FirstOrDefault(), out Guid id))
                {
                    return id;
                }
            }

            return null;
        }
    }
}