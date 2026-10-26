using System.Text.Json;
using Middleware.Logging.Models;

namespace Middleware.Logging.Extensions
{
    public static class StringExtentions
    {
        public static bool TryParseErrorResponse(this string jsonData, out ErrorResponseModel? result)
        {
            try
            {
                result = JsonSerializer.Deserialize<ErrorResponseModel>(jsonData);
                return true;
            }
            catch (Exception)
            {
                result = null;
                return false;
            }
        }
    }
}