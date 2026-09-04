using Microsoft.AspNetCore.Mvc;

namespace Builder.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    
    private readonly WeatherForceCastBuilder _weatherForceCastBuilder;

    public WeatherForecastController(WeatherForceCastBuilder weatherForceCastBuilder)
    {
        _weatherForceCastBuilder = weatherForceCastBuilder;
    }

    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    [HttpGet(Name = "GetWeatherForecast")]
    public IEnumerable<WeatherForecast> Get()
    {
        return Enumerable.Range(1, 5).Select(index => _weatherForceCastBuilder.New()
            .WithDate(DateOnly.FromDateTime(DateTime.Now.AddDays(index)))
            .WithTemperatureC(Random.Shared.Next(-20, 55))
            .WithSummary(Summaries[Random.Shared.Next(Summaries.Length)])
            .Build())
            .ToArray();
    }
}