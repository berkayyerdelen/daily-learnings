using System.ComponentModel.DataAnnotations;
using ApiLogging.Api.Logging;
using Microsoft.AspNetCore.Mvc;

namespace ApiLogging.Api.Controllers;

public sealed record CreateOrderRequest(
    [Required] string CustomerId,
    [Range(0.01, 1_000_000)] decimal Amount,
    [Required] string CardNumber,
    string? Notes = null);

public sealed record OrderResponse(Guid Id, string CustomerId, decimal Amount, DateTimeOffset CreatedAt);

public sealed record LoginRequest(string UserName, string Password);

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    // No storage on purpose: the point of this project is the logging pipeline, and a demo that
    // keeps every order in a static dictionary would itself leak memory under a load test.
    [HttpPost]
    public ActionResult<OrderResponse> Create(CreateOrderRequest request)
    {
        var order = new OrderResponse(Guid.NewGuid(), request.CustomerId, request.Amount, DateTimeOffset.UtcNow);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<OrderResponse> Get(Guid id) => NotFound();

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("Simulated failure");
}

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    // Credentials must never reach the log table.
    [SkipApiLog]
    [HttpPost("login")]
    public IActionResult Login(LoginRequest request) => Ok(new { token = "demo-token" });
}
