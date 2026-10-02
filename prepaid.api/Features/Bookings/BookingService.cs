using System.Collections.Concurrent;

namespace prepaid.api.Features.Bookings;

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
