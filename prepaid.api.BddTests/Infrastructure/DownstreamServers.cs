using WireMock.Server;

namespace prepaid.api.BddTests.Infrastructure;

public class DownstreamServers : IDisposable
{
    public WireMockServer Server { get; } = WireMockServer.Start();

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
    }
}
