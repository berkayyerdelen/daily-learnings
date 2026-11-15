using System.Net;
using System.Net.Http.Json;
using ApiLogging.Api.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ApiLogging.Tests;

public sealed class ApiLogFilterTests : IDisposable
{
    private readonly FakeApiLogRepository _repository = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiLogFilterTests() : this(maxQueueBytes: null) { }

    private ApiLogFilterTests(long? maxQueueBytes, bool logAllEndpoints = false)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            if (logAllEndpoints) b.UseSetting("ApiLogging:LogAllEndpoints", "true");
            if (maxQueueBytes is not null) b.UseSetting("ApiLogging:MaxQueueBytes", maxQueueBytes.ToString());
            b.UseSetting("ApiLogging:FlushInterval", "00:00:00.050");
            b.UseSetting("ApiLogging:FlushInterval", "00:00:00.050");
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IApiLogRepository>(_repository);
                s.AddControllers().AddApplicationPart(typeof(TestController).Assembly);
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Logs_request_and_response()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { customerId = "c-42", amount = 10.5m, cardNumber = "1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await _repository.WaitForAsync(1);
        var log = Assert.Single(_repository.Logs);
        Assert.Equal("POST", log.Method);
        Assert.Equal("/api/orders", log.Path);
        Assert.Equal(201, log.StatusCode);
        Assert.Contains("\"customerId\":\"c-42\"", log.RequestBody);
        Assert.Contains("\"amount\":10.5", log.ResponseBody);
    }

    [Fact]
    public async Task Logs_validation_400_and_exception_500()
    {
        await _client.PostAsJsonAsync("/api/orders", new { customerId = "", amount = 0 });
        await _client.GetAsync("/api/orders/boom");

        await _repository.WaitForAsync(2);
        Assert.Contains(_repository.Logs, l => l.StatusCode == 400);
        Assert.Contains(_repository.Logs, l => l.StatusCode == 500 && l.Exception!.Contains("Simulated failure"));
    }

    [Fact]
    public async Task Skips_endpoints_marked_SkipApiLog()
    {
        await _client.PostAsJsonAsync("/api/auth/login", new { userName = "a", password = "secret" });
        await _client.GetAsync($"/api/orders/{Guid.Empty}");

        await _repository.WaitForAsync(1);
        await Task.Delay(150);
        Assert.Equal(404, Assert.Single(_repository.Logs).StatusCode);
    }

    [Fact]
    public async Task Bodies_are_not_truncated()
    {
        await _client.GetAsync("/test/big?size=100000");

        await _repository.WaitForAsync(1);
        // Exactly what the client received: MVC writes a string result as text/plain, without quotes.
        Assert.Equal(new string('x', 100_000), Assert.Single(_repository.Logs).ResponseBody);
    }

    [Fact]
    public async Task Long_paths_are_logged_in_full()
    {
        // Longer than the old nvarchar(2048) column: must be stored as-is, never cut.
        var longPath = "/test/any/" + new string('p', 5_000);
        var response = await _client.GetAsync(longPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _repository.WaitForAsync(1);
        Assert.Equal(longPath, Assert.Single(_repository.Logs).Path);
    }

    [Fact]
    public async Task A_body_that_cannot_be_serialized_does_not_break_the_request()
    {
        var response = await _client.PostAsJsonAsync("/test/cycle", new { name = "a" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _repository.WaitForAsync(1);
    }

    [Fact]
    public async Task Lazy_queries_are_not_executed_a_second_time_for_the_log()
    {
        TestController.QueryExecutions = 0;
        await _client.GetAsync("/test/query");

        await _repository.WaitForAsync(1);
        Assert.Equal(3, TestController.QueryExecutions); // only MVC's enumeration of 3 items
        // The body is copied from what MVC wrote, so the query result is logged without re-running it.
        Assert.Equal("[0,1,2]", Assert.Single(_repository.Logs).ResponseBody);
    }

    [Fact]
    public async Task Streamed_responses_reach_the_client_but_are_not_buffered_for_the_log()
    {
        var body = await _client.GetStringAsync("/test/stream");
        Assert.Equal("[0,1,2]", body);

        await _repository.WaitForAsync(1);
        Assert.Contains("is streamed", Assert.Single(_repository.Logs).ResponseBody);
    }

    [Fact]
    public async Task Drops_logs_over_the_memory_budget_and_releases_it_after_insert()
    {
        using var sut = new ApiLogFilterTests(maxQueueBytes: 300_000);
        var budget = sut._factory.Services.GetRequiredService<ApiLogMemoryBudget>();

        await sut._client.GetAsync("/test/big?size=100000"); // ~200 KB: fits
        await sut._repository.WaitForAsync(1);
        Assert.Equal(0, budget.UsedBytes); // released after the batch was inserted

        // Two at once while nothing is flushed yet would exceed the budget...
        await Task.WhenAll(sut._client.GetAsync("/test/big?size=100000"), sut._client.GetAsync("/test/big?size=100000"));
        await sut._repository.WaitForAsync(2);
        await Task.Delay(150);
        Assert.Equal(0, budget.UsedBytes);
        Assert.InRange(sut._repository.Logs.Count, 2, 3); // at least one of the two concurrent ones fits
    }

    [Fact]
    public async Task Only_endpoints_with_ApiLog_are_logged_and_SkipApiLog_overrides_the_controller()
    {
        await _client.GetAsync("/optin/plain");    // no attribute: not logged
        await _client.GetAsync("/optin/logged");   // [ApiLog] on the action: logged
        await _client.GetAsync("/optout/skipped"); // [SkipApiLog] beats the controller's [ApiLog]
        await _client.GetAsync("/optout/logged");  // [ApiLog] on the controller: logged

        await _repository.WaitForAsync(2);
        await Task.Delay(150);
        Assert.Equal(new[] { "/optin/logged", "/optout/logged" }, _repository.Logs.Select(l => l.Path).Order());
    }

    [Fact]
    public async Task LogAllEndpoints_logs_endpoints_without_the_attribute_but_still_honours_SkipApiLog()
    {
        using var sut = new ApiLogFilterTests(maxQueueBytes: null, logAllEndpoints: true);

        await sut._client.GetAsync("/optin/plain");
        await sut._client.GetAsync("/optout/skipped");

        await sut._repository.WaitForAsync(1);
        await Task.Delay(150);
        Assert.Equal("/optin/plain", Assert.Single(sut._repository.Logs).Path);
    }

    [Theory]
    [InlineData("ApiLogging:BatchSize", "10000")]   // above the lock-escalation-safe cap
    [InlineData("ApiLogging:WriterCount", "0")]
    [InlineData("ApiLogging:Capacity", "0")]
    public void Invalid_settings_stop_the_app_at_startup(string key, string value)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting(key, value);
            b.ConfigureServices(s => s.AddSingleton<IApiLogRepository>(new FakeApiLogRepository()));
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(key.Split(':')[1], error.ToString());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
