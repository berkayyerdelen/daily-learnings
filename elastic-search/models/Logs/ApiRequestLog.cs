using System;

namespace Middleware.Logging.Models.Logs
{
    public class ApiRequestLog : BaseLog
    {
        public DateTime RequestTimestamp { get; set; }
        
        public string ResponseBody { get; set; }
    }    
}