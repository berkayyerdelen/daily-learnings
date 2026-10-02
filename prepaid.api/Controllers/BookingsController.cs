using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace prepaid.api.Controllers;

[ApiController]
[Route("[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    public async Task<IActionResult> InsertBooking([FromBody] Booking booking)
    {
        await _bookingService.Save(booking);
        return Ok(booking);
    }

    [HttpGet("{uniqueId}")]
    public async Task<IActionResult> GetBooking(string uniqueId)
    {
        var booking = await _bookingService.Get(uniqueId);
        if (booking == null)
        {
            return NotFound("Booking not found.");
        }
        return Ok(booking);
    }
}

public class Booking
{
    public string BookingId { get; set; }
    public string CustomerName { get; set; }
    public decimal Amount { get; set; }
}

public interface IBookingService
{
    Task Save(Booking booking);
    Task<Booking?> Get(string uniqueId);
}

public class BookingService : IBookingService
{
    private readonly ConcurrentDictionary<string, Booking> _bookings = new(new Dictionary<string, Booking>
    {
        ["1"] = new Booking { BookingId = "1", CustomerName = "John Doe", Amount = 100.00m },
        ["2"] = new Booking { BookingId = "2", CustomerName = "Jane Smith", Amount = 200.00m },
        ["3"] = new Booking { BookingId = "3", CustomerName = "Alice Johnson", Amount = 150.00m }
    });

    public Task Save(Booking booking)
    {
        _bookings[booking.BookingId] = booking;
        return Task.CompletedTask;
    }

    public Task<Booking?> Get(string uniqueId)
    {
        _bookings.TryGetValue(uniqueId, out var booking);
        return Task.FromResult(booking);
    }
}
