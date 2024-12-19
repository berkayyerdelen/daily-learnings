using System.Net.Sockets;
using System.Text;
using actor_models.configuration;
using actor_models.contracts;

namespace actor_models.implementations;

public class GarageDoorGrain : Grain, IDoorOpeningService
{
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private GarageDoorConfig _config;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var doorId = this.GetPrimaryKeyString();

        // Load configuration for this door (replace with actual data source)
        _config = await LoadConfigAsync(doorId);

        if (_config != null)
        {
            // Establish TCP connection
            _tcpClient = new TcpClient(_config.IpAddress, _config.Port);
            _stream = _tcpClient.GetStream();
        }
        else
        {
            throw new KeyNotFoundException($"No configuration found for door: {doorId}");
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _tcpClient?.Close();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }


    private Task<GarageDoorConfig> LoadConfigAsync(string doorId)
    {
        // Mock configuration store (replace with DB or file system)
        var mockConfigStore = new Dictionary<string, GarageDoorConfig>
        {
            { "Door1", new GarageDoorConfig { DoorId = "Door1", IpAddress = "192.168.1.10", Port = 12345 } },
            { "Door2", new GarageDoorConfig { DoorId = "Door2", IpAddress = "192.168.1.11", Port = 12346 } }
        };

        return Task.FromResult(mockConfigStore.ContainsKey(doorId) ? mockConfigStore[doorId] : null);
    }

    public async Task OpenDoor(string doorId)
    {
        if (_stream != null && _config != null)
        {
            var command = Array.Empty<byte>();
            await _stream.WriteAsync(command, 0, command.Length);
        }
        else
        {
            throw new InvalidOperationException("Garage door is not properly configured or connected.");
        }
    }
}