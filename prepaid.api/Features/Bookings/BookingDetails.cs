using prepaid.api.Clients.CustomerDirectory;

namespace prepaid.api.Features.Bookings;

public class BookingDetails
{
    public Booking Booking { get; set; }
    public CustomerProfile? CustomerProfile { get; set; }
}
