using prepaid.api.BddTests.Support;
using Reqnroll;

namespace prepaid.api.BddTests.Hooks;

[Binding]
public class TestHooks
{
    private readonly ScenarioContext _scenarioContext;

    public TestHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario]
    public void CreateFactory()
    {
        var factory = new CustomWebApplicationFactory();
        _scenarioContext.Set(factory);
        _scenarioContext.Set(factory.CreateClient());
    }

    [AfterScenario]
    public void DisposeFactory()
    {
        if (_scenarioContext.TryGetValue(out CustomWebApplicationFactory? factory))
        {
            factory!.Dispose();
        }
    }
}
