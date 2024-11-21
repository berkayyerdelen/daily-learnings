using event_sourcing.aggregateroot;
using MongoDB.Driver;

namespace event_sourcing.repositories.contracts;

public interface ISnapshotStoreRepository
{
  Task SaveSnapshot(Guid accountId, decimal balance);
}