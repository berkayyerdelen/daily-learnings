using Microsoft.AspNetCore.Mvc;

namespace Factory.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{

    private readonly IBookingFactory _bookingFactory;

    public WeatherForecastController(IBookingFactory bookingFactory)
    {
        _bookingFactory = bookingFactory;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var booking = _bookingFactory.Create("The Two Towers");
        booking.Print();
        return Ok();
    }
}


public interface IBookingFactory
{
    IBooking Create(string book);
}
public class BookingFactory : IBookingFactory
{
    public IBooking Create(string book)
    {
        return book switch
        {
            "The Two Towers" => new TheTwoTowers(),
            "The Return of the King" => new TheReturnOfTheKingStrategy(),
            _ => throw new ArgumentException($"Invalid book: {book}")
        };
    }
}

public interface IBooking
{
    public void Print() => Console.WriteLine($"Booking for {GetType().Name}");
}

public class TheTwoTowers : IBooking
{
    public void Print() => Console.WriteLine($"Booking for {GetType().Name}");
}

public class TheReturnOfTheKingStrategy : IBooking
{
  
}