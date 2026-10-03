using System.Net.Http.Json;
using System.Text.Json;

namespace prepaid.api;

public class PaymentService : IPaymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(HttpClient httpClient, ILogger<PaymentService> logger)
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
