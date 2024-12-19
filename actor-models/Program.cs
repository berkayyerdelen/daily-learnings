using actor_models.contracts;
using actor_models.implementations;
using Orleans.Configuration;


var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering()
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "dev"; // Should match client ClusterId
            options.ServiceId = "GarageService"; // Should match client ServiceId
        });
});
builder.Services.AddSingleton<IDoorOpeningService, GarageDoorGrain>();
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/open-door", async (IGrainFactory grainFactory) =>
{
    var doorGrain = grainFactory.GetGrain<IDoorOpeningService>("Door1");
    await doorGrain.OpenDoor("Door1");
    return Results.Ok("Door opened successfully.");
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}