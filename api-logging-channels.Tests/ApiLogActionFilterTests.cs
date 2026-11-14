using System.Net;
using System.Net.Http.Json;
using ApiLogging.Api.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ApiLogging.Tests;

public sealed class ApiLogActionFilterTests : IDisposable
{
    private readonly InMemoryApiLogStore _store = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiLogActionFilterTests() : this(bodySampleRate: "1.0") { }

    private ApiLogActionFilterTests(string bodySampleRate)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ApiLogging:BodySampleRate", bodySampleRate);
            builder.UseSetting("ApiLogging:FlushInterval", "00:00:00.050");
            builder.UseSetting("ApiLogging:MaxBodyLength", "200");
            builder.ConfigureServices(services => services.AddSingleton<IApiLogStore>(_store));
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Logs_request_and_response_and_masks_sensitive_fields()
    {
        var response = await _client.PostAsJsonAsync("/api/orders",
            new { customerId = "c-42", amount = 10.5m, cardNumber = "4111111111111111" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await _store.WaitForEntriesAsync(1);
        var log = Assert.Single(_store.Entries);

        Assert.Equal("POST", log.HttpMethod);
        Assert.Equal("/api/orders", log.Path);
        Assert.Equal("Orders.Create", log.Endpoint);
        Assert.Equal(201, log.StatusCode);
        Assert.Contains("\"customerId\":\"c-42\"", log.RequestBody);
        Assert.Contains("\"cardNumber\":\"***\"", log.RequestBody);
        Assert.DoesNotContain("4111111111111111", log.RequestBody);
        Assert.Contains("\"amount\":10.5", log.ResponseBody);
        Assert.Null(log.Exception);
        Assert.False(string.IsNullOrEmpty(log.TraceId));
    }

    [Fact]
    public async Task Logs_automatic_400_from_model_validation()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { customerId = "", amount = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await _store.WaitForEntriesAsync(1);
        var log = Assert.Single(_store.Entries);
        Assert.Equal(400, log.StatusCode);
        Assert.Contains("errors", log.ResponseBody);
    }

    [Fact]
    public async Task Logs_404_from_action_result()
    {
        await _client.GetAsync($"/api/orders/{Guid.Empty}");

        await _store.WaitForEntriesAsync(1);
        Assert.Equal(404, Assert.Single(_store.Entries).StatusCode);
    }

    [Fact]
    public async Task Logs_unhandled_exception_as_500()
    {
        var response = await _client.GetAsync("/api/orders/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await _store.WaitForEntriesAsync(1);
        var log = Assert.Single(_store.Entries);
        Assert.Equal(500, log.StatusCode);
        Assert.Contains("Simulated failure", log.Exception);
        Assert.Null(log.ResponseBody);
    }

    [Fact]
    public async Task Truncates_large_bodies()
    {
        await _client.PostAsJsonAsync("/api/orders",
            new { customerId = new string('x', 1_000), amount = 1m, cardNumber = "1" });

        await _store.WaitForEntriesAsync(1);
        var log = Assert.Single(_store.Entries);
        Assert.EndsWith("...[truncated]", log.RequestBody);
        Assert.Equal(200 + "...[truncated]".Length, log.RequestBody!.Length);
    }

    [Fact]
    public async Task Skips_endpoints_marked_SkipApiLog()
    {
        await _client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = "secret" });
        await _client.GetAsync($"/api/orders/{Guid.Empty}"); // marker request

        await _store.WaitForEntriesAsync(1);
        await Task.Delay(150);
        Assert.Equal("/api/orders/" + Guid.Empty, Assert.Single(_store.Entries).Path);
    }

    [Fact]
    public async Task With_sampling_off_keeps_metadata_but_bodies_only_for_errors()
    {
        using var sut = new ApiLogActionFilterTests(bodySampleRate: "0");

        await sut._client.PostAsJsonAsync("/api/orders", new { customerId = "c-1", amount = 1m, cardNumber = "4111" });
        await sut._client.PostAsJsonAsync("/api/orders", new { customerId = "", amount = 0 });

        await sut._store.WaitForEntriesAsync(2);
        var ok = Assert.Single(sut._store.Entries, e => e.StatusCode == 201);
        var bad = Assert.Single(sut._store.Entries, e => e.StatusCode == 400);

        Assert.Null(ok.RequestBody);
        Assert.Null(ok.ResponseBody);
        Assert.Contains("\"customerId\":\"\"", bad.RequestBody);
        Assert.Contains("errors", bad.ResponseBody);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
