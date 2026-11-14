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

    public ApiLogFilterTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ApiLogging:FlushInterval", "00:00:00.050");
            b.ConfigureServices(s => s.AddSingleton<IApiLogRepository>(_repository));
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

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
