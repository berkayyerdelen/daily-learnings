namespace event_sourcing.requests;

public class WithdrawRequest
{
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}