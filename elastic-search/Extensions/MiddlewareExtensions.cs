

using Middleware.Logging.Middleware;

namespace Middleware.Logging.Extensions
{
    public static class MiddleWareExtensions
    {
        public static IApplicationBuilder UseErrorLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ErrorLoggingMiddleWare>();
        }

        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestLoggingMiddleWare>();
        }
    }
}
