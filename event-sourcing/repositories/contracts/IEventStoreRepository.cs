using event_sourcing.events;

namespace event_sourcing.repositories.contracts;

public interface IEventStoreRepository
{
    Task SaveEvents(Guid aggregateId, IEnumerable<DomainEvent> events);
    Task<IEnumerable<DomainEvent>> GetEventsForAggregate(Guid aggregateId);
}