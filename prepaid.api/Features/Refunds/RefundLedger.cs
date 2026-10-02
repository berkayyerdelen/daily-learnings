using System.Collections.Concurrent;

namespace prepaid.api.Features.Refunds;

public interface IRefundLedger
{
  bool TryApplyRefund(Guid transactionId, decimal amount, decimal paymentAmount, out decimal totalRefunded);
}

public class InMemoryRefundLedger : IRefundLedger
{
  private readonly ConcurrentDictionary<Guid, decimal> _refundedByTransaction = new();

  public bool TryApplyRefund(Guid transactionId, decimal amount, decimal paymentAmount, out decimal totalRefunded)
  {
    while (true)
    {
      var current = _refundedByTransaction.GetOrAdd(transactionId, 0m);
      var attempted = current + amount;
      if (attempted > paymentAmount)
      {
        totalRefunded = current;
        return false;
      }

      if (_refundedByTransaction.TryUpdate(transactionId, attempted, current))
      {
        totalRefunded = attempted;
        return true;
      }
    }
  }
}
