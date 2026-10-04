using System.ComponentModel.DataAnnotations;

namespace prepaid.api.Clients.Payments;

public class PaymentApiOptions
{
    [Required]
    public string BaseUrl { get; set; } = string.Empty;
}
