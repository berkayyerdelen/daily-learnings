using event_sourcing.events;
using MongoDB.Bson.Serialization.Attributes;

namespace event_sourcing.shared_kernel;

[BsonIgnoreExtraElements]
public class StoredEvent
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; }
    public DomainEvent EventData { get; set; }
    public DateTime OccuredOn { get; set; }
    
}