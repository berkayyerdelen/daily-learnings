using System.Net.Http.Json;
using FluentAssertions;
using prepaid.api;
using prepaid.api.BddTests.Support;
using Reqnroll;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace prepaid.api.BddTests.StepDefinitions;

[Binding]
public class RefundSteps
{
    private readonly ScenarioContext _scenarioContext;

    public RefundSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"a payment exists with transaction id ""(.*)"" and amount (\d+)")]
    public void GivenAPaymentExistsWithTransactionIdAndAmount(string transactionId, decimal amount)
    {
        var factory = _scenarioContext.Get<CustomWebApplicationFactory>();
        factory.PaymentApiServer
            .Given(Request.Create().WithPath($"/payments/{transactionId}").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { transactionId, amount }));
    }

    [When(@"I refund (\d+) for transaction ""(.*)""")]
    public async Task WhenIRefundForTransaction(decimal amount, string transactionId)
    {
        var client = _scenarioContext.Get<HttpClient>();
        var response = await PostRefundAsync(client, transactionId, amount);
        _scenarioContext.Set(response);
    }

    [Then(@"the refund should be successful")]
    public async Task ThenTheRefundShouldBeSuccessful()
    {
        var body = await GetRefundResponseAsync();
        body.IsSuccessful.Should().BeTrue();
    }

    [Then(@"the refund amount should be (\d+)")]
    public async Task ThenTheRefundAmountShouldBe(decimal amount)
    {
        var body = await GetRefundResponseAsync();
        body.Amount.Should().Be(amount);
    }

    private static async Task<HttpResponseMessage> PostRefundAsync(HttpClient client, string transactionId, decimal amount)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/Refunds")
        {
            Content = JsonContent.Create(new RefundRequest
            {
                TransactionId = transactionId,
                Amount = amount
            })
        };
        requestMessage.Headers.Add("X-Api-Key", CustomWebApplicationFactory.TestApiKey);
        return await client.SendAsync(requestMessage);
    }

    private async Task<RefundResponse> GetRefundResponseAsync()
    {
        if (_scenarioContext.TryGetValue(out RefundResponse? cached))
        {
            return cached!;
        }

        var response = _scenarioContext.Get<HttpResponseMessage>();
        var body = await response.Content.ReadFromJsonAsync<RefundResponse>();
        _scenarioContext.Set(body);
        return body!;
    }
}
