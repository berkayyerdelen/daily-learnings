using System.Net.Http.Json;
using FluentAssertions;
using prepaid.api.Features.Bookings;
using Reqnroll;

namespace prepaid.api.BddTests.StepDefinitions;

[Binding]
public class BookingSteps
{
    private readonly ScenarioContext _scenarioContext;

    public BookingSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [When(@"I insert a booking with id ""(.*)"", customer ""(.*)"" and amount (\d+)")]
    public async Task WhenIInsertABookingWithIdCustomerAndAmount(string id, string customerName, decimal amount)
    {
        var client = _scenarioContext.Get<HttpClient>();
        var response = await client.PostAsJsonAsync("/Bookings", new Booking
        {
            BookingId = id,
            CustomerName = customerName,
            Amount = amount
        });
        response.IsSuccessStatusCode.Should().BeTrue("the booking insert should have succeeded");
    }

    [When(@"I request booking ""(.*)""")]
    public async Task WhenIRequestBooking(string id)
    {
        var client = _scenarioContext.Get<HttpClient>();
        var response = await client.GetAsync($"/Bookings/{id}");
        _scenarioContext.Set(response);
    }

    [Then(@"the response status should be (\d+)")]
    public void ThenTheResponseStatusShouldBe(int statusCode)
    {
        var response = _scenarioContext.Get<HttpResponseMessage>();
        ((int)response.StatusCode).Should().Be(statusCode);
    }

    [Then(@"the customer name should be ""(.*)""")]
    public async Task ThenTheCustomerNameShouldBe(string customerName)
    {
        var response = _scenarioContext.Get<HttpResponseMessage>();
        var details = await response.Content.ReadFromJsonAsync<BookingDetails>();
        details!.Booking.CustomerName.Should().Be(customerName);
    }
}
