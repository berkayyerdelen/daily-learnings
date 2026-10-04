using System.ComponentModel.DataAnnotations;

namespace prepaid.api.Clients.CustomerDirectory;

public class CustomerDirectoryOptions
{
    [Required]
    public string BaseUrl { get; set; } = string.Empty;
}
