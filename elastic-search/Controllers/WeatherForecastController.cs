using Microsoft.AspNetCore.Mvc;


namespace elastic_search.Controllers;

[ApiController]
[Route("api")]
public class WeatherForecastController : ControllerBase
{
    private readonly ILogger<WeatherForecastController> _logger;

    public WeatherForecastController(ILogger<WeatherForecastController> logger)
    {
        _logger = logger;
    }

    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    [HttpGet("1")]
    public IEnumerable<WeatherForecast> Get()
    {
        var orderId = 123;
        _logger.LogInformation(
            $"Fetching order {orderId}");
        _logger.LogError(
            new Exception("Failed to fetch order"), $"Failed to fetch order {orderId}");
        throw new Exception($"Failed to fetch order {orderId} global");
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
    }
    [HttpGet("2")]
    public IEnumerable<WeatherForecast> Get2()
    {
        var orderId = 123;
        _logger.LogInformation(
            $"Fetching order {orderId}");
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
    }
}

