using System.Net.Http.Json;
using FluentAssertions;
using prepaid.api.BddTests.MockScenarios;
using prepaid.api.BddTests.Support;
using prepaid.api.Features.Bookings;
using Reqnroll;

namespace prepaid.api.BddTests.StepDefinitions;

[Binding]
public class CustomerDirectorySteps
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ScenarioContext _scenarioContext;

    public CustomerDirectorySteps(CustomWebApplicationFactory factory, ScenarioContext scenarioContext)
    {
        _factory = factory;
        _scenarioContext = scenarioContext;
    }

    [Given(@"a customer profile exists for ""(.*)"" with email ""(.*)"" and loyalty tier ""(.*)""")]
    public void GivenACustomerProfileExistsForWithEmailAndLoyaltyTier(string customerId, string email, string loyaltyTier)
    {
        CustomerDirectoryScenarios.Found(_factory.DownstreamServers.Server, customerId, email, loyaltyTier);
    }

    [Given(@"the customer directory service is unavailable")]
    public void GivenTheCustomerDirectoryServiceIsUnavailable()
    {
        CustomerDirectoryScenarios.Unavailable(_factory.DownstreamServers.Server);
    }

    [Then(@"the customer email should be ""(.*)""")]
    public async Task ThenTheCustomerEmailShouldBe(string email)
    {
        var response = _scenarioContext.Get<HttpResponseMessage>();
        var details = await response.Content.ReadFromJsonAsync<BookingDetails>();
        details!.CustomerProfile.Should().NotBeNull();
        details.CustomerProfile!.Email.Should().Be(email);
    }

    [Then(@"no customer profile should be returned")]
    public async Task ThenNoCustomerProfileShouldBeReturned()
    {
        var response = _scenarioContext.Get<HttpResponseMessage>();
        var details = await response.Content.ReadFromJsonAsync<BookingDetails>();
        details!.CustomerProfile.Should().BeNull();
    }
}
