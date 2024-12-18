using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;
using StackExchange.Redis;
using YourApp.Messages;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);


// Connect to Redis

var redisConnection = ConnectionMultiplexer.Connect("redis:6379");

// Add Redis as a singleton service
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// Add Redis caching
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "redis:6379"; // Use the Docker service name
    options.InstanceName = "YourApp_";
});

// Add Serilog
builder.Host.UseSerilog();

// Add MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
    // Register the consumer
    x.AddConsumer<ExampleConsumer>();

    // Configure RabbitMQ with the connection settings
    x.UsingRabbitMq((context, cfg) =>
    {
        // Replace "rabbitmq" with the hostname if using Docker or the correct hostname
        cfg.Host("rabbitmq", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Configure the receive endpoint where the consumer will listen
        cfg.ReceiveEndpoint("example-queue", e =>
        {
            // Ensure concurrency limit is set on the receive endpoint
            e.ConfigureConsumer<ExampleConsumer>(context);

            // Set a concurrency limit to process a maximum of 10 messages concurrently
            e.UseConcurrencyLimit(10);
        });
    });
});

// Add the hosted service to run the background consumer task


builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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
    .WithName("GetWeatherForecast");

app.MapGet("/api/messages", async (
    [FromQuery] string text, 
    IPublishEndpoint publishEndpoint,
    CancellationToken cancellationToken) =>
{
    // Publish messages in a loop
    for (int i = 0; i < 1_000_000; i++)
    {
        await publishEndpoint.Publish(new YourMessage($"text: counter {i}"), cancellationToken);
    }

    return Results.Ok("Messages published");
});

// Custom logging for status codes
// app.UseStatusCodePages(async context =>
// {
//     var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
//     var statusCode = context.HttpContext.Response.StatusCode;
//
//     if (statusCode == 404)
//     {
//         var path = context.HttpContext.Request.Path;
//         logger.LogWarning("404 Not Found: {Path}", path);
//     }
// });

// Run the application
app.Run();

// Record definition
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// MassTransit consumer definition

public class ExampleConsumer : IConsumer<YourMessage>
{
    // Semaphore to control the number of concurrent database calls per consumer
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(5000);  // Limit to 5000 concurrent operations
    
    private static int _currentlyProcessingCount = 0;

    // Inject IConnectionMultiplexer for Redis usage
    private readonly IConnectionMultiplexer _redisConnection;

    public ExampleConsumer(IConnectionMultiplexer redisConnection)
    {
        _redisConnection = redisConnection;
    }

    public async Task Consume(ConsumeContext<YourMessage> context)
    {
        var message = context.Message;
        
        // Log the received message and the current processing count
        Log.Information("Received message: {Message}, Currently Processing: {Count}", message.Text, _currentlyProcessingCount);

        // Increment the processing count
        Interlocked.Increment(ref _currentlyProcessingCount);

        // Wait for a slot to become available in the semaphore before proceeding with a resource-intensive task (like DB call)
        await _semaphore.WaitAsync();

        try
        {
            // Optionally, you can use Redis to track if the message is being processed
            var redisDb = _redisConnection.GetDatabase();
            
            // Check if the message is already being processed (use the message text as a key)
            var isProcessing = await redisDb.StringGetAsync(message.Text);
            if (isProcessing.HasValue && isProcessing == "Processing")
            {
                Log.Information("Message is already being processed: {Message}", message.Text);
                return;  // Skip processing if already in progress
            }

            // Set Redis flag to indicate the message is being processed
            await redisDb.StringSetAsync(message.Text, "Processing", TimeSpan.FromMinutes(10));  // Set a 10-minute expiration

            // Simulate a 10-second delay to represent a resource-intensive task (like DB call)
            // await Task.Delay(TimeSpan.FromSeconds(10));

            // Log after the delay (processing done)
            Log.Information("Processed message: {Message}, Currently Processing: {Count}", message.Text, _currentlyProcessingCount);

            // Optionally, store or cache some results in Redis here
            // await redisDb.StringSetAsync(message.Text, "Processed", TimeSpan.FromHours(1)); // Cache processed result

        }
        finally
        {
            // Release the semaphore slot when done
            _semaphore.Release();
        }

        // Decrement the processing count after the task completes
        Interlocked.Decrement(ref _currentlyProcessingCount);
    }
}


public class RedisService
{
    private readonly IDistributedCache _cache;

    public RedisService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task SetCacheAsync(string key, string value)
    {
        await _cache.SetStringAsync(key, value);
    }

    public async Task<string> GetCacheAsync(string key)
    {
        return await _cache.GetStringAsync(key);
    }
}

// Message definition

namespace YourApp.Messages
{
    public class YourMessage
    {
        public string Text { get; set; }

        public YourMessage(string text)
        {
            Text = text;
        }
    }
}