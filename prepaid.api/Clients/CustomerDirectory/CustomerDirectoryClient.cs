using System.Net.Http.Json;
using System.Text.Json;

namespace prepaid.api.Clients.CustomerDirectory;

public interface ICustomerDirectoryClient
{
    Task<CustomerProfile> GetProfile(string customerId, CancellationToken cancellationToken);
}

public class CustomerProfile
{
    public string CustomerId { get; set; }
    public string Email { get; set; }
    public string LoyaltyTier { get; set; }
    public bool Verified { get; set; }
}

public class CustomerDirectoryClient : ICustomerDirectoryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CustomerDirectoryClient> _logger;

    public CustomerDirectoryClient(HttpClient httpClient, ILogger<CustomerDirectoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CustomerProfile> GetProfile(string customerId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/customers/{Uri.EscapeDataString(customerId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Customer directory lookup for {CustomerId} returned {StatusCode}", customerId, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();

        var profile = await response.Content.ReadFromJsonAsync<CustomerProfile>(JsonOptions, cancellationToken);
        return profile!;
    }
}
