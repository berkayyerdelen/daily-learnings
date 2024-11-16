using System.Text.Json;
using redis_pub_sub.services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddHostedService<MessageSubscriberService>();
builder.Services.AddHostedService<MessageSubscriberService2>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.MapPost("/publish", async (RedisService redisService) =>
{
    var orderCreated = new OrderCreated()
    {
        OrderId = Guid.NewGuid(),
    };

    var serializedOrderCreated = JsonSerializer.Serialize(orderCreated);
    var subscriber = redisService.GetSubscriber();
    await subscriber.PublishAsync("order-created", new RedisValue(serializedOrderCreated));
});


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

class OrderCreated
{
    public Guid OrderId { get; set; }
}

public class MessageSubscriberService : BackgroundService
{
    private readonly RedisService _redisService;

    public MessageSubscriberService(RedisService redisService)
    {
        _redisService = redisService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redisService.GetSubscriber();

        await subscriber.SubscribeAsync("order-created", (channel, message) =>
        {
            Console.WriteLine($"Received message2: {message}");
        });
    }
}

public class MessageSubscriberService2 : BackgroundService
{
    private readonly RedisService _redisService;

    public MessageSubscriberService2(RedisService redisService)
    {
        _redisService = redisService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redisService.GetSubscriber();

        await subscriber.SubscribeAsync("order-created", (channel, message) =>
        {
            Console.WriteLine($"Received message: {message}");
        });
    }
}