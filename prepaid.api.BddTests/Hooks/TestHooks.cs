using Reqnroll.BoDi;
using prepaid.api.BddTests.Infrastructure;
using prepaid.api.BddTests.Support;
using Reqnroll;

namespace prepaid.api.BddTests.Hooks;

[Binding]
public class TestHooks
{
    private static DownstreamServers? _downstreamServers;

    private readonly IObjectContainer _objectContainer;

    public TestHooks(IObjectContainer objectContainer)
    {
        _objectContainer = objectContainer;
    }

    [BeforeTestRun]
    public static void StartDownstreamServers()
    {
        _downstreamServers = new DownstreamServers();
    }

    [AfterTestRun]
    public static void StopDownstreamServers()
    {
        _downstreamServers?.Dispose();
        _downstreamServers = null;
    }

    [BeforeScenario]
    public void CreateFactory()
    {
        var downstreamServers = _downstreamServers
            ?? throw new InvalidOperationException("Downstream servers were not started for this test run.");
        downstreamServers.Server.Reset();

        var factory = new CustomWebApplicationFactory(downstreamServers);
        _objectContainer.RegisterInstanceAs(factory);
        _objectContainer.RegisterInstanceAs(factory.CreateClient());
    }

    [AfterScenario]
    public void DisposeFactory()
    {
        _objectContainer.Resolve<CustomWebApplicationFactory>().Dispose();
    }
}
