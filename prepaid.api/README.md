# prepaid.api — keeping BDD tests in sync with code changes

This only covers the test side: whenever something changes in `prepaid.api`, what has to change in `prepaid.api.BddTests` to keep covering it. BDD stack: Reqnroll + WireMock.Net, driven through `WebApplicationFactory<Program>` (same `Program.cs` the real app uses — nothing about the app is swapped out except downstream HTTP endpoints).

Test project layout:

```
prepaid.api.BddTests/
  Features/            .feature files + generated step-bindings (*.feature.cs)
  StepDefinitions/      Given/When/Then bindings
  MockScenarios/        WireMock stub builders (Found/NotFound/Unavailable per downstream)
  Infrastructure/
    DownstreamServers.cs   one WireMockServer per real downstream dependency
  Support/
    CustomWebApplicationFactory.cs   boots the app in-memory; this is the test's "appsettings"
    ScenarioState.cs
  Hooks/
    TestHooks.cs         creates a fresh factory + HttpClient per scenario
```

## If you add a new endpoint

No new downstream dependency → no config changes. Just:

1. Add a `.feature` scenario under `Features/` describing it.
2. Add the `Given`/`When`/`Then` bindings under `StepDefinitions/` that call it through the scenario's `HttpClient` (`_scenarioContext.Get<HttpClient>()`, set up by `TestHooks`).

Concrete reference: `BookingSteps.WhenIRequestBooking` sends the request, `BookingSteps.ThenTheResponseStatusShouldBe` / `ThenTheCustomerNameShouldBe` assert on the response — copy that shape for a new endpoint's steps.

If the new endpoint returns a different response shape than an existing one, add/adjust the `record`/`class` you deserialize into (e.g. `BookingDetails`) in the step definition — the test project doesn't share DTOs with the app project automatically beyond what you reference via the `ProjectReference`.

## If you add a new downstream service

This is where config comes in. `CustomWebApplicationFactory` is the test-time equivalent of `appsettings.json` — it overrides config via `ConfigureWebHost(...).ConfigureAppConfiguration(...)`, pointing every `<Service>:BaseUrl` key at a local WireMock instance instead of a real host.

**You don't need a new WireMock server per downstream service.** WireMock matches on request path, not host, so one shared instance can serve every downstream dependency — `/payments/*` and `/customers/*` never collide. `Infrastructure/DownstreamServers.cs` is just:

```csharp
public class DownstreamServers : IDisposable
{
    public WireMockServer Server { get; } = WireMockServer.Start();

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
    }
}
```

Only reach for a second server if a new dependency's paths could plausibly collide with an existing one, or you specifically want its request log isolated for debugging — neither applies to `Payments`/`CustomerDirectory` here.

Checklist for adding a new downstream service, with the real `CustomerDirectory` addition as the worked example (this already happened in this repo — `Clients/CustomerDirectory/CustomerDirectoryClient.cs` + `CustomerDirectory:BaseUrl` in `appsettings.json`):

**1. Point the app's config at the shared server** — `Support/CustomWebApplicationFactory.cs`. Every config key the real `Program.cs` reads for the new client (`"<Service>:BaseUrl"`) needs a matching entry here, or the in-process app will try to call the real/placeholder URL from `appsettings.json` and fail:

```csharp
config.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["PaymentApi:BaseUrl"] = DownstreamServers.Server.Url,
    ["CustomerDirectory:BaseUrl"] = DownstreamServers.Server.Url,   // same shared server
    ["Refunds:ApiKey"] = TestApiKey
});
```

**2. Add a mock-scenario helper** — `MockScenarios/<Service>Scenarios.cs`, static methods that stub the common response shapes (see `CustomerDirectoryScenarios.cs`: `Found`, `NotFound`, `Unavailable`, `Timeout`):

```csharp
public static void Found(WireMockServer server, string customerId, string email, string loyaltyTier, bool verified = true)
{
    server
        .Given(Request.Create().WithPath($"/customers/{customerId}").UsingGet())
        .RespondWith(Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBodyAsJson(new { customerId, email, loyaltyTier, verified }));
}
```

**3. Add step definitions** that call the helper — `StepDefinitions/<Service>Steps.cs` (see `CustomerDirectorySteps.cs`):

```csharp
[Given(@"a customer profile exists for ""(.*)"" with email ""(.*)"" and loyalty tier ""(.*)""")]
public void GivenACustomerProfileExistsForWithEmailAndLoyaltyTier(string customerId, string email, string loyaltyTier)
{
    CustomerDirectoryScenarios.Found(_factory.DownstreamServers.Server, customerId, email, loyaltyTier);
}
```

Inject `CustomWebApplicationFactory` (and `ScenarioContext` if you need the last response) via the constructor — Reqnroll resolves it through its DI container.

**4. ⚠️ Register the factory instance, or the stub goes to the wrong server.** Reqnroll's container will happily *auto-construct* its own `CustomWebApplicationFactory` for any step class that asks for one in its constructor, instead of reusing the one `TestHooks` already created — and that second instance spins up its own, different WireMock servers. If your new step class takes `CustomWebApplicationFactory` as a constructor parameter, it only gets the right instance because `TestHooks.CreateFactory` explicitly registers it into the container:

```csharp
[BeforeScenario]
public void CreateFactory()
{
    var factory = new CustomWebApplicationFactory();
    _objectContainer.RegisterInstanceAs(factory);   // <- without this, constructor-injected factories are each a separate instance
    _scenarioContext.Set(factory);
    _scenarioContext.Set(factory.CreateClient());
}
```

This is already in place — just don't remove it, and don't assume a second step-definition class constructor-injecting `CustomWebApplicationFactory` is somehow unsafe; it isn't, as long as this registration stays.

**5. Add the `.feature` scenario(s)** exercising the new dependency's success and failure paths (see `BookingEnrichment.feature`: one scenario where the directory returns a profile, one where it's unavailable and the response degrades gracefully instead of failing).

## If a new step is added to an existing flow

Example: a fraud check gets inserted into `RefundService.RefundAsync`, between the payment lookup and the ledger update.

1. **Don't let it silently break existing scenarios.** If the new step calls a new downstream service, every *existing* scenario that exercises that flow (e.g. `BookingRefundFlow.feature`'s refund scenario) now also calls that service — WireMock's default for an unstubbed request is a 404/`ConnectionRefused`-style failure, which will make previously-passing scenarios fail. Either:
   - add that service's "happy path" stub inside the shared `Given` step the scenario already uses (so it's always stubbed), or
   - add an explicit new `Given` step to the existing scenario (e.g. `Given the transaction is not flagged by the fraud check`) and update the `.feature` file.
2. **Add a new scenario for the step's rejection path** — same shape as step 5 above, but asserting the new failure response:
   ```gherkin
   Scenario: Refund blocked by fraud check
     Given a payment exists with transaction id "..." and amount 100
     And the transaction is flagged by the fraud check
     When I refund 25 for transaction "..."
     Then the response status should be 400
   ```
3. **New config only if the step calls a new downstream dependency** — then it's the exact same checklist as "If you add a new downstream service" above (`DownstreamServers.cs`, `CustomWebApplicationFactory.cs` config entry, mock scenarios, step defs). If the step is pure in-process logic, no config changes are needed — just the new step definitions and scenario(s).

## Running the suite

```
dotnet test prepaid.api.BddTests/prepaid.api.BddTests.csproj
```

No real network calls and no real downstream service needs to be up — every dependency is a local WireMock instance started per scenario.
