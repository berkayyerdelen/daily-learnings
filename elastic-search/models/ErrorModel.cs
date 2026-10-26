using System.Text.Json.Serialization;

namespace Middleware.Logging.Models
{
    public class ErrorModel
    {
        public string ErrorMessage { get; set; }
        public string ErrorCode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ErrorProperty> Properties { get; set; }
    }
}