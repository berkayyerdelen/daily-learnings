using WireMock.Server;

namespace prepaid.api.BddTests.Infrastructure;

public static class WireMockServerExtensions
{
    public static int CallsTo(this WireMockServer server, string pathPrefix) =>
        server.LogEntries.Count(l => l.RequestMessage.Path.StartsWith(pathPrefix));
}
