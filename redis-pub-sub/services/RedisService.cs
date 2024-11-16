using StackExchange.Redis;

namespace redis_pub_sub.services;

public class RedisService
{
    private readonly Lazy<ConnectionMultiplexer> _connectionMultiplexer;

    public RedisService()
    {
        _connectionMultiplexer = new Lazy<ConnectionMultiplexer>(() =>
        {
            var config = "localhost:6379"; // Replace with your Redis connection string
            return ConnectionMultiplexer.Connect(config);
        });
    }

    private  ConnectionMultiplexer Connection => _connectionMultiplexer.Value;

    public IDatabase GetDatabase() => Connection.GetDatabase();

    public ISubscriber GetSubscriber() => Connection.GetSubscriber();

}