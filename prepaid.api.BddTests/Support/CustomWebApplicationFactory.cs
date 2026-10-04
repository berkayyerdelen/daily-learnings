using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using prepaid.api.BddTests.Infrastructure;

namespace prepaid.api.BddTests.Support;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key";

    public DownstreamServers DownstreamServers { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentApi:BaseUrl"] = DownstreamServers.Server.Url,
                ["CustomerDirectory:BaseUrl"] = DownstreamServers.Server.Url,
                ["Refunds:ApiKey"] = TestApiKey
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        DownstreamServers.Dispose();
        base.Dispose(disposing);
    }
}
