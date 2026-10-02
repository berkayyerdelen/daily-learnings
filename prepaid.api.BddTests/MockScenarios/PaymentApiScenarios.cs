using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace prepaid.api.BddTests.MockScenarios;

public static class PaymentApiScenarios
{
    public static void Found(WireMockServer server, string transactionId, decimal amount)
    {
        server
            .Given(Request.Create().WithPath($"/payments/{transactionId}").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { transactionId, amount }));
    }

    public static void NotFound(WireMockServer server, string transactionId)
    {
        server
            .Given(Request.Create().WithPath($"/payments/{transactionId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
    }

    public static void Unavailable(WireMockServer server, string transactionId)
    {
        server
            .Given(Request.Create().WithPath($"/payments/{transactionId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));
    }
}
