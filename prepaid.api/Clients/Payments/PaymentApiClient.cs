using System.Net.Http.Json;
using System.Text.Json;

namespace prepaid.api.Clients.Payments;

public interface IPaymentApiClient
{
    Task<Payment> Payment(Guid transactionId, CancellationToken cancellationToken);
}

public class Payment
{
    public string TransactionId { get; set; }
    public decimal Amount { get; set; }
}

public class PaymentApiClient : IPaymentApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentApiClient> _logger;

    public PaymentApiClient(HttpClient httpClient, ILogger<PaymentApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Payment> Payment(Guid transactionId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/payments/{transactionId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Payment lookup for {TransactionId} returned {StatusCode}", transactionId, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();

        var payment = await response.Content.ReadFromJsonAsync<Payment>(JsonOptions, cancellationToken);
        return payment!;
    }
}
