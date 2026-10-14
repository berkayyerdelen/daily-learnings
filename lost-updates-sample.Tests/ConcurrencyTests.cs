using System.Net;
using System.Net.Http.Json;
using LostUpdates.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LostUpdates.Tests;

public sealed class ConcurrencyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ConcurrencyTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Atomic_decrements_preserve_every_successful_update()
    {
        await _factory.ResetDataAsync();
        using var client = _factory.CreateClient();

        const int requestCount = 30;
        var tasks = Enumerable.Range(0, requestCount).Select(_ =>
            client.PostAsync("/inventory/1/decrement?amount=1", content: null));

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quantity = await db.InventoryItems.Where(x => x.Id == 1).Select(x => x.Quantity).SingleAsync();
        Assert.Equal(100 - requestCount, quantity);
    }

    [Fact]
    public async Task Two_order_updates_with_same_version_only_allow_one()
    {
        await _factory.ResetDataAsync();
        using var client = _factory.CreateClient();

        var first = client.PutAsJsonAsync("/orders/1", new { address = "Address A", version = 1 });
        var second = client.PutAsJsonAsync("/orders/1", new { address = "Address B", version = 1 });
        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Stale_order_version_returns_conflict()
    {
        await _factory.ResetDataAsync();
        using var client = _factory.CreateClient();

        var first = await client.PutAsJsonAsync("/orders/1", new { address = "Updated", version = 1 });
        var stale = await client.PutAsJsonAsync("/orders/1", new { address = "Stale overwrite", version = 1 });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
