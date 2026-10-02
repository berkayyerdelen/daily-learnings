using Microsoft.AspNetCore.Mvc;
using prepaid.api.Clients.CustomerDirectory;

namespace prepaid.api.Features.Bookings;

[ApiController]
[Route("[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;
    private readonly ICustomerDirectoryClient _customerDirectoryClient;
    private readonly ILogger<BookingsController> _logger;

    public BookingsController(
        IBookingService bookingService,
        ICustomerDirectoryClient customerDirectoryClient,
        ILogger<BookingsController> logger)
    {
        _bookingService = bookingService;
        _customerDirectoryClient = customerDirectoryClient;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> InsertBooking([FromBody] Booking booking)
    {
        await _bookingService.Save(booking);
        return Ok(booking);
    }

    [HttpGet("{uniqueId}")]
    public async Task<IActionResult> GetBooking(string uniqueId, CancellationToken cancellationToken)
    {
        var booking = await _bookingService.Get(uniqueId);
        if (booking == null)
        {
            return NotFound("Booking not found.");
        }

        CustomerProfile? profile = null;
        try
        {
            profile = await _customerDirectoryClient.GetProfile(booking.CustomerName, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Customer directory enrichment failed for booking {BookingId}; returning booking without profile", uniqueId);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Customer directory enrichment timed out for booking {BookingId}; returning booking without profile", uniqueId);
        }

        return Ok(new BookingDetails { Booking = booking, CustomerProfile = profile });
    }
}
