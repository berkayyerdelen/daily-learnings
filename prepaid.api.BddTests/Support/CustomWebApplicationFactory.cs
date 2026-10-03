using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WireMock.Server;

namespace prepaid.api.BddTests.Support;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key";

    public WireMockServer PaymentApiServer { get; } = WireMockServer.Start();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentApi:BaseUrl"] = PaymentApiServer.Url,
                ["Refunds:ApiKey"] = TestApiKey
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        PaymentApiServer.Stop();
        PaymentApiServer.Dispose();
        base.Dispose(disposing);
    }
}
