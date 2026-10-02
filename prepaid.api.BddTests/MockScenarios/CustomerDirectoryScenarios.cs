using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace prepaid.api.BddTests.MockScenarios;

public static class CustomerDirectoryScenarios
{
    public static void Found(WireMockServer server, string customerId, string email, string loyaltyTier, bool verified = true)
    {
        server
            .Given(Request.Create().WithPath($"/customers/{customerId}").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { customerId, email, loyaltyTier, verified }));
    }

    public static void NotFound(WireMockServer server, string customerId)
    {
        server
            .Given(Request.Create().WithPath($"/customers/{customerId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
    }

    public static void Unavailable(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/customers/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));
    }

    public static void Timeout(WireMockServer server, string customerId, int delayMilliseconds = 15000)
    {
        server
            .Given(Request.Create().WithPath($"/customers/{customerId}").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromMilliseconds(delayMilliseconds)));
    }
}
