namespace event_sourcing.events;

public class DomainEvent
{
    public DateTime OccuredOn { get; private set; }

    protected DomainEvent()
    {
        OccuredOn = DateTime.Now;
    }
}

public class AccountCreatedDomainEvent : DomainEvent
{
    public AccountCreatedDomainEvent(Guid accountId, decimal initialBalance)
    {
        AccountId = accountId;
        InitialBalance = initialBalance;
    }

    public Guid AccountId { get; set; }
    public decimal InitialBalance { get; set; }
    
}

public class MoneyDepositedEvent : DomainEvent
{
    public MoneyDepositedEvent(Guid accountId, decimal amount)
    {
        AccountId = accountId;
        Amount = amount;
    }

    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}

public class MoneyWithdrawalEvent : DomainEvent
{
    public MoneyWithdrawalEvent(Guid accountId, decimal amount)
    {
        AccountId = accountId;
        Amount = amount;
    }

    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}

