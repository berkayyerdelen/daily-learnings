using System.Net;

namespace prepaid.api;

public class RefundService : IRefundService
{
  private readonly IPaymentService _paymentService;
  private readonly IRefundLedger _refundLedger;
  private readonly ILogger<RefundService> _logger;

  public RefundService(IPaymentService paymentService, IRefundLedger refundLedger, ILogger<RefundService> logger)
  {
    _paymentService = paymentService;
    _refundLedger = refundLedger;
    _logger = logger;
  }

  public async Task<RefundResponse> RefundAsync(RefundRequest request, CancellationToken cancellationToken)
  {
    if (!Guid.TryParse(request.TransactionId, out var transactionId))
    {
      _logger.LogWarning("Refund rejected: invalid transaction id '{TransactionId}'", request.TransactionId);
      return Failure("Invalid transaction id.");
    }

    if (request.Amount <= 0)
    {
      _logger.LogWarning("Refund rejected: non-positive amount {Amount} for transaction {TransactionId}", request.Amount, transactionId);
      return Failure("Refund amount must be positive.");
    }

    Payment payment;
    try
    {
      payment = await _paymentService.Payment(transactionId, cancellationToken);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
      _logger.LogWarning("Refund rejected: payment {TransactionId} not found", transactionId);
      return Failure("Payment not found.");
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "Payment service call failed for transaction {TransactionId}", transactionId);
      return Failure("Payment service unavailable.");
    }

    if (!_refundLedger.TryApplyRefund(transactionId, request.Amount, payment.Amount, out var totalRefunded))
    {
      _logger.LogWarning("Refund rejected: amount {Amount} exceeds refundable balance for transaction {TransactionId}", request.Amount, transactionId);
      return Failure("Refund amount exceeds refundable balance.");
    }

    _logger.LogInformation("Refunded {Amount} for transaction {TransactionId} (total refunded {TotalRefunded})", request.Amount, transactionId, totalRefunded);
    return new RefundResponse { IsSuccessful = true, Amount = request.Amount };
  }

  private static RefundResponse Failure(string error) => new() { IsSuccessful = false, Amount = 0, Error = error };
}

public interface IPaymentService
{
  Task<Payment> Payment(Guid transactionId, CancellationToken cancellationToken);
}

public class Payment
{
  public string TransactionId { get; set; }
  public decimal Amount { get; set; }
}

public interface IRefundService
{
  Task<RefundResponse> RefundAsync(RefundRequest request, CancellationToken cancellationToken);
}

public class RefundRequest
{
  public string TransactionId { get; set; }
  public decimal Amount { get; set; }
}

public class RefundResponse
{
  public bool IsSuccessful { get; set; }
  public decimal Amount { get; set; }
  public string? Error { get; set; }
}
