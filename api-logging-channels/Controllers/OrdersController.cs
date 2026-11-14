using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using ApiLogging.Api.Logging;
using Microsoft.AspNetCore.Mvc;

namespace ApiLogging.Api.Controllers;

// Validation attributes go on the constructor parameters (MVC validates those for records);
// [SensitiveData] goes on the property, which is what System.Text.Json serializes.
public sealed record CreateOrderRequest(
    [Required] string CustomerId,
    [Range(0.01, 1_000_000)] decimal Amount,
    [Required][property: SensitiveData] string CardNumber);

public sealed record OrderResponse(Guid Id, string CustomerId, decimal Amount, DateTimeOffset CreatedAt);

public sealed record LoginRequest(string UserName, string Password);

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    // Demo storage only; the point of this project is the logging pipeline.
    private static readonly ConcurrentDictionary<Guid, OrderResponse> Orders = new();

    [HttpPost]
    public ActionResult<OrderResponse> Create(CreateOrderRequest request)
    {
        var order = new OrderResponse(Guid.NewGuid(), request.CustomerId, request.Amount, DateTimeOffset.UtcNow);
        Orders[order.Id] = order;
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<OrderResponse> Get(Guid id) =>
        Orders.TryGetValue(id, out var order) ? order : NotFound();

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("Simulated failure");
}

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    // Credentials must never reach the log table, not even masked.
    [SkipApiLog]
    [HttpPost("login")]
    public IActionResult Login(LoginRequest request) => Ok(new { token = "demo-token" });
}
