
using Serilog;

namespace Middleware.Logging.Extensions
{
    public static class SerilogExtensions
    {
        public static void AddSerilog(this IServiceCollection services, IConfiguration configuration, string loggerName = "Logger")
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            var logger = new LoggerFactory().AddSerilog(Log.Logger).CreateLogger(loggerName);

            services.AddSingleton(logger);
        }
    }
}
