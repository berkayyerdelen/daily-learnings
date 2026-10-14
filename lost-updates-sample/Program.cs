using LostUpdates.Api.Data;
using LostUpdates.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();

// For a compact demo, migrate and seed at startup. For production, run migrations
// as a deployment step rather than having every API replica migrate on boot.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!await db.InventoryItems.AnyAsync())
    {
        db.InventoryItems.Add(new InventoryItem { Id = 1, Name = "Demo item", Quantity = 100 });
    }

    if (!await db.Orders.AnyAsync())
    {
        db.Orders.Add(new Order
        {
            Id = 1,
            Address = "1 Example Street",
            DeliveryInstructions = "Leave at reception",
            Version = 1
        });
    }

    await db.SaveChangesAsync();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Lost Updates Sample",
    endpoints = new[]
    {
        "GET /inventory/{id}",
        "POST /inventory/{id}/broken-decrement?amount=1",
        "POST /inventory/{id}/decrement?amount=1",
        "GET /orders/{id}",
        "PUT /orders/{id} with { address, version }"
    }
}));

app.MapGet("/inventory/{id:int}", async (int id, AppDbContext db) =>
{
    var item = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    return item is null
        ? Results.NotFound()
        : Results.Ok(new { item.Id, item.Name, item.Quantity });
});

// INTENTIONALLY UNSAFE: read-modify-write. Demonstrates a lost update.
app.MapPost("/inventory/{id:int}/broken-decrement", async (
    int id, int amount, AppDbContext db, CancellationToken ct) =>
{
    if (amount <= 0) return Results.BadRequest(new { message = "amount must be positive" });

    var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (item is null) return Results.NotFound();

    if (item.Quantity < amount)
        return Results.Conflict(new { message = "Not enough inventory" });

    // Deliberately yield after reading to make overlapping requests more likely in tests.
    await Task.Yield();
    item.Quantity -= amount;
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { item.Id, item.Quantity, approach = "unsafe-read-modify-write" });
});

// SAFE: one atomic SQL statement. The quantity check prevents negative stock.
app.MapPost("/inventory/{id:int}/decrement", async (
    int id, int amount, AppDbContext db, CancellationToken ct) =>
{
    if (amount <= 0) return Results.BadRequest(new { message = "amount must be positive" });

    var affected = await db.InventoryItems
        .Where(x => x.Id == id && x.Quantity >= amount)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Quantity, x => x.Quantity - amount), ct);

    if (affected == 0)
    {
        var exists = await db.InventoryItems.AnyAsync(x => x.Id == id, ct);
        return exists
            ? Results.Conflict(new { message = "Not enough inventory" })
            : Results.NotFound();
    }

    var quantity = await db.InventoryItems
        .Where(x => x.Id == id)
        .Select(x => x.Quantity)
        .SingleAsync(ct);

    return Results.Ok(new { id, quantity, approach = "atomic-update" });
});

app.MapGet("/orders/{id:int}", async (int id, AppDbContext db) =>
{
    var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    return order is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            order.Id,
            order.Address,
            order.DeliveryInstructions,
            order.Version
        });
});

// Optimistic concurrency: update only if the client's version still matches.
app.MapPut("/orders/{id:int}", async (
    int id, UpdateOrderRequest request, AppDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Address))
        return Results.BadRequest(new { message = "address is required" });

    var affected = await db.Orders
        .Where(x => x.Id == id && x.Version == request.Version)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Address, request.Address)
            .SetProperty(x => x.Version, x => x.Version + 1), ct);

    if (affected == 0)
    {
        var exists = await db.Orders.AnyAsync(x => x.Id == id, ct);
        return exists
            ? Results.Conflict(new
            {
                message = "Order changed since it was loaded. Fetch the latest version and merge your changes."
            })
            : Results.NotFound();
    }

    var updated = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == id, ct);
    return Results.Ok(new { updated.Id, updated.Address, updated.Version });
});

app.Run();

public partial class Program;
