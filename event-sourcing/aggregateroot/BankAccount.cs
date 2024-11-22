using event_sourcing.events;
using MongoDB.Bson.Serialization.Attributes;

namespace event_sourcing.aggregateroot;

[BsonIgnoreExtraElements]
public class BankAccount
{
    private List<DomainEvent> _domainEvents = new List<DomainEvent>();
    private decimal _balance;
    public BankAccount(Guid id)
    {
        Id = id;
        _balance = 0;
    }

    public Guid Id { get; set; }

    public decimal Balance => _balance;

    private void Apply(DomainEvent @event)
    {
        switch (@event)
        {
            case AccountCreatedDomainEvent accountCreated:
                _balance = accountCreated.InitialBalance;
                break;
            case MoneyDepositedEvent deposited:
                _balance += deposited.Amount;
                break;
            case MoneyWithdrawalEvent withdrawal:
                if (_balance >= withdrawal.Amount)
                {
                    _balance -= withdrawal.Amount;
                }
                else
                {
                    throw new InvalidOperationException("Insufficient funds");
                }
                break;
        }
    }

    public long VersionId => VersionId;

    public IEnumerable<DomainEvent> GetUncommittedDomainEvents()
    {
        return _domainEvents;
    }

    public void MarkChangesAsCommitted()
    {
        _domainEvents.Clear();
    }

    public void Deposit(decimal amount)
    {
        var depositEvent = new MoneyDepositedEvent(Id, amount);
        Apply(depositEvent);
        _domainEvents.Add(depositEvent);
    }

    public void Withdraw(decimal amount)
    {
        var withDrawEvent = new MoneyWithdrawalEvent(Id, amount);
        Apply(withDrawEvent);
        _domainEvents.Add(withDrawEvent);
    }

    public static BankAccount Rehydrate(Guid accountId,IEnumerable<DomainEvent> events)
    {
        var account = new BankAccount(accountId);

        foreach (var @event in events)
        {
            account.Apply(@event);
        }

        return account;
    }
}