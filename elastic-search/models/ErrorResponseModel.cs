using System.Collections.Generic;

namespace Middleware.Logging.Models
{
    public class ErrorResponseModel
    {
        public IEnumerable<ErrorModel> Errors { get; set; }
    }
}