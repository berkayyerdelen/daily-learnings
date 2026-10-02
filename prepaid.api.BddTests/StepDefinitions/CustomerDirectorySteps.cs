using prepaid.api.BddTests.MockScenarios;
using prepaid.api.BddTests.Support;
using Reqnroll;

namespace prepaid.api.BddTests.StepDefinitions;

[Binding]
public class CustomerDirectorySteps
{
    private readonly CustomWebApplicationFactory _factory;

    public CustomerDirectorySteps(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Given(@"a customer profile exists for ""(.*)"" with email ""(.*)"" and loyalty tier ""(.*)""")]
    public void GivenACustomerProfileExistsForWithEmailAndLoyaltyTier(string customerId, string email, string loyaltyTier)
    {
        CustomerDirectoryScenarios.Found(_factory.DownstreamServers.CustomerDirectory, customerId, email, loyaltyTier);
    }

    [Given(@"the customer directory service is unavailable")]
    public void GivenTheCustomerDirectoryServiceIsUnavailable()
    {
        CustomerDirectoryScenarios.Unavailable(_factory.DownstreamServers.CustomerDirectory);
    }
}
