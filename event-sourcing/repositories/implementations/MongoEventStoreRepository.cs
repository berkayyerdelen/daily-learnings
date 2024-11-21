using event_sourcing.events;
using event_sourcing.repositories.contracts;
using event_sourcing.shared_kernel;
using MongoDB.Driver;

namespace event_sourcing.repositories.implementations;

public class MongoEventStoreRepository : IMongoEventStoreRepository
{
    private readonly IMongoCollection<StoredEvent> _mongoCollection;


    public MongoEventStoreRepository()
    {
        //for now pass it as hard-coded
        var mongoClient = new MongoClient("mongodb://localhost:27017");
        var database = mongoClient.GetDatabase("events");
        _mongoCollection = database.GetCollection<StoredEvent>("events");
    }
    
    public async Task SaveEvents(Guid aggregateId, IEnumerable<DomainEvent> events)
    {
        var storedEvents = events.Select(x => new StoredEvent()
        {
            AggregateId = aggregateId,
            EventType = x.GetType().Name,
            EventData = x,
            OccuredOn = x.OccuredOn
        });
        
        await _mongoCollection.InsertManyAsync(storedEvents);
    }

    public async Task<IEnumerable<DomainEvent>> GetEventsForAggregate(Guid aggregateId)
    {
        var storedEvents = await _mongoCollection.Find(x => x.AggregateId == aggregateId).SortBy(x => x.OccuredOn)
            .ToListAsync();
        
        return storedEvents.Select(x=>x.EventData);
    }
}