using LostUpdates.Api.Data;
using LostUpdates.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LostUpdates.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Tests use their own database. ResetDataAsync drops it, so never point this at your dev database.
    private const string DefaultConnection =
        "Host=localhost;Port=5433;Database=lost_updates_sample_tests;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                ?? DefaultConnection;
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connection
            });
        });
    }

    public async Task ResetDataAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        db.InventoryItems.Add(new InventoryItem { Id = 1, Name = "Test item", Quantity = 100 });
        db.Orders.Add(new Order
        {
            Id = 1,
            Address = "Original address",
            DeliveryInstructions = "Reception",
            Version = 1
        });
        await db.SaveChangesAsync();
    }
}
