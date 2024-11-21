namespace event_sourcing.requests;

public class DepositRequest
{
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}