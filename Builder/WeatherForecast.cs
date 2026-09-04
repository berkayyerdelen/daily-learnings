namespace Builder;

public class WeatherForecast
{
    public DateOnly Date { get; set; }

    public int TemperatureC { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

    public string? Summary { get; set; }
}

public class WeatherForceCastBuilder
{
    private WeatherForecast _weatherForecast;

    public WeatherForceCastBuilder New()
    {
        _weatherForecast = new WeatherForecast();
        return this;
    }

    public WeatherForceCastBuilder WithDate(DateOnly date)
    {
        _weatherForecast.Date = date;
        return this;
    }
    
    public WeatherForceCastBuilder WithTemperatureC(int temperatureC)
    {
        _weatherForecast.TemperatureC = temperatureC;
        return this;
    }
    
    public WeatherForceCastBuilder WithSummary(string summary)
    {
        _weatherForecast.Summary = summary;
        return this;
    }
    
    public WeatherForecast Build()
    {
        return _weatherForecast;
    }
}