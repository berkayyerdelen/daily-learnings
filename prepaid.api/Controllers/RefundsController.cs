using Microsoft.AspNetCore.Mvc;
using prepaid.api.Security;

namespace prepaid.api.Controllers;

[ApiController]
[Route("[controller]")]
[ApiKeyAuth]
public class RefundsController : ControllerBase
{
    private readonly IRefundService _refundService;

    public RefundsController(IRefundService refundService)
    {
        _refundService = refundService;
    }

    [HttpPost]
    public async Task<IActionResult> Refund([FromBody] RefundRequest request, CancellationToken cancellationToken)
    {
        var response = await _refundService.RefundAsync(request, cancellationToken);
        return response.IsSuccessful ? Ok(response) : BadRequest(response);
    }
}
