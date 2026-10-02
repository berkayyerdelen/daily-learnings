using WireMock.Server;

namespace prepaid.api.BddTests.Infrastructure;

public class DownstreamServers : IDisposable
{
    public WireMockServer Payments { get; } = WireMockServer.Start();
    public WireMockServer CustomerDirectory { get; } = WireMockServer.Start();

    public void Dispose()
    {
        Payments.Stop();
        Payments.Dispose();
        CustomerDirectory.Stop();
        CustomerDirectory.Dispose();
    }
}
