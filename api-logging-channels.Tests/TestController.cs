using ApiLogging.Api.Logging;
using Microsoft.AspNetCore.Mvc;

namespace ApiLogging.Tests;

public sealed class Node
{
    public string Name { get; set; } = "";
    public Node? Self { get; set; }
}

/// <summary>Test-only endpoints for edge cases the demo controller does not have.</summary>
[ApiLog]
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

    [HttpGet("stream")]
    public async IAsyncEnumerable<int> Stream()
    {
        for (var i = 0; i < 3; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    [HttpGet("big")]
    public IActionResult Big([FromQuery] int size) => Ok(new string('x', size));
}

/// <summary>No [ApiLog] on the controller: only the action that opts in is logged.</summary>
[ApiController]
[Route("optin")]
public sealed class OptInController : ControllerBase
{
    [HttpGet("plain")]
    public IActionResult Plain() => Ok("not logged unless LogAllEndpoints");

    [ApiLog]
    [HttpGet("logged")]
    public IActionResult Logged() => Ok("logged");
}

/// <summary>[ApiLog] on the controller, with one action opting out.</summary>
[ApiLog]
[ApiController]
[Route("optout")]
public sealed class OptOutController : ControllerBase
{
    [HttpGet("logged")]
    public IActionResult Logged() => Ok("logged");

    [SkipApiLog]
    [HttpGet("skipped")]
    public IActionResult Skipped() => Ok("never logged");
}
