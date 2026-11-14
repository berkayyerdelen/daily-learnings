using Microsoft.AspNetCore.Mvc;

namespace ApiLogging.Tests;

public sealed class Node
{
    public string Name { get; set; } = "";
    public Node? Self { get; set; }
}

/// <summary>Test-only endpoints for edge cases the demo controller does not have.</summary>
[ApiController]
[Route("test")]
public sealed class TestController : ControllerBase
{
    public static int QueryExecutions;

    [HttpGet("cycle")]
    public IActionResult Cycle()
    {
        var node = new Node { Name = "a" };
        node.Self = node;
        return Ok(new { node.Name }); // MVC response is fine; the log copy has the cycle below
    }

    [HttpPost("cycle")]
    public IActionResult CycleBody(Node node) => Ok(node.Name);

    [HttpGet("query")]
    public IActionResult Query()
    {
        // Each enumeration counts as one "database query".
        var query = Enumerable.Range(0, 3).Select(i => { Interlocked.Increment(ref QueryExecutions); return i; }).AsQueryable();
        return Ok(query);
    }

    [HttpGet("big")]
    public IActionResult Big([FromQuery] int size) => Ok(new string('x', size));
}
