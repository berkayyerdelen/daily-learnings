using Reqnroll.BoDi;
using prepaid.api.BddTests.Support;
using Reqnroll;

namespace prepaid.api.BddTests.Hooks;

[Binding]
public class TestHooks
{
    private readonly ScenarioContext _scenarioContext;
    private readonly IObjectContainer _objectContainer;

    public TestHooks(ScenarioContext scenarioContext, IObjectContainer objectContainer)
    {
        _scenarioContext = scenarioContext;
        _objectContainer = objectContainer;
    }

    [BeforeScenario]
    public void CreateFactory()
    {
        var factory = new CustomWebApplicationFactory();
        _objectContainer.RegisterInstanceAs(factory);
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
