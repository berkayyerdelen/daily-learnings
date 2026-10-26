using System;

namespace Middleware.Logging.Models.Logs
{
    public class BaseLog
    {
        public string ClientId { get; set; }
        
        public Guid? CorrelationId { get; set; }
        
        public string Endpoint { get; set; }
        
        public string Headers { get; set; }
        
        public string IdentityId { get; set; }
        
        public string Method { get; set; }
        
        public string Parameters { get; set; }
        
        public string RemoteIp { get; set; }
        
        public string RequestBody { get; set; }
        
        public int ResponseStatus { get; set; }
        
        public long RequestDuration { get; set; }

        public string UserId { get; set; }
    }
}