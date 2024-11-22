using event_sourcing.aggregateroot;
using event_sourcing.repositories.contracts;
using MongoDB.Driver;

namespace event_sourcing.repositories.implementations;

public class SnapshotStoreRepository : ISnapshotStoreRepository
{
    private readonly IMongoCollection<AccountSnapShot> _mongoCollection;

    public SnapshotStoreRepository()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("snapshots");

        _mongoCollection = database.GetCollection<AccountSnapShot>("account-snapshots");
    }

    public Task SaveSnapshot(Guid accountId, decimal balance)
    {
        var accountSnapShot = new AccountSnapShot()
        {
            AccountId = accountId,
            Balance = balance,
            LastUpdated = DateTime.UtcNow
        };
        return _mongoCollection.ReplaceOneAsync(x=>x.AccountId == accountId, accountSnapShot, new ReplaceOptions { IsUpsert = true });
    }

    public async Task<decimal> GetSnapshot(Guid accountId)
    {
        var snapshot= await _mongoCollection
            .Find(x=>x.AccountId == accountId)
            .FirstOrDefaultAsync();
        return snapshot.Balance;
    }
}