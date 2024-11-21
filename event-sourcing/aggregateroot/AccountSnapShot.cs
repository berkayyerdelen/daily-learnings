using MongoDB.Bson.Serialization.Attributes;

namespace event_sourcing.aggregateroot;

[BsonIgnoreExtraElements]
public class AccountSnapShot
{
    public Guid AccountId { get; set; }
    public decimal Balance { get; set; }
    public DateTime LastUpdated { get; set; }
}