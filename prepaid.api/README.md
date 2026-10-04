# prepaid.api — keeping BDD tests in sync with code changes

This only covers the test side: whenever something changes in `prepaid.api`, what has to change in `prepaid.api.BddTests` to keep covering it. BDD stack: Reqnroll + WireMock.Net, driven through `WebApplicationFactory<Program>` (same `Program.cs` the real app uses — nothing about the app is swapped out except downstream HTTP endpoints).

Test project layout:

```
prepaid.api.BddTests/
  Features/            .feature files + generated step-bindings (*.feature.cs)
  StepDefinitions/      Given/When/Then bindings
  MockScenarios/        WireMock stub builders (Found/NotFound/Unavailable per downstream)
  Infrastructure/
    DownstreamServers.cs   one shared WireMockServer, started once for the whole test run
  Support/
    CustomWebApplicationFactory.cs   boots the app in-memory; this is the test's "appsettings"
    ScenarioState.cs
  Hooks/
    TestHooks.cs         starts/stops the shared WireMock server for the run; fresh app host per scenario
  xunit.runner.json      forces serial execution — required because the WireMock server is shared mutable state
```

The WireMock server is started once in `[BeforeTestRun]` and stopped in `[AfterTestRun]` (`TestHooks.cs`) — not per scenario. Each `[BeforeScenario]` calls `Server.Reset()` (clears mappings + request log) and builds a fresh `CustomWebApplicationFactory`/app host, so every scenario still starts from a clean slate without paying WireMock's startup cost each time. This only works because `xunit.runner.json` disables collection parallelization — Reqnroll puts each `.feature` file in its own test class, and xUnit runs different classes in parallel by default; with one shared server, a scenario in another class calling `Reset()` mid-run would wipe out stubs this scenario just set up. Don't remove `xunit.runner.json` without re-introducing per-scenario WireMock instances.

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

`TestHooks` owns one instance of this for the entire run (`[BeforeTestRun]`/`[AfterTestRun]`) and passes it into each scenario's `CustomWebApplicationFactory`; it does not construct a new one per scenario.

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
    var downstreamServers = _downstreamServers ?? throw new InvalidOperationException(...);
    downstreamServers.Server.Reset();   // clean slate, but keep the server itself running

    var factory = new CustomWebApplicationFactory(downstreamServers);
    _objectContainer.RegisterInstanceAs(factory);                 // <- without this, constructor-injected factories are each a separate instance
    _objectContainer.RegisterInstanceAs(factory.CreateClient());
}
```

This is already in place — just don't remove it. Every step class should get `CustomWebApplicationFactory` and `HttpClient` the *same* way: as plain constructor parameters (see `RefundSteps`/`CustomerDirectorySteps`/`BookingSteps`). There's no second, parallel way to reach them (no `ScenarioContext.Get<CustomWebApplicationFactory>()` or `ScenarioContext.Get<HttpClient>()` anywhere) — one resolution path, so it's not possible to accidentally end up on a different instance. Keep `ScenarioContext` only for values that genuinely change step-to-step within a scenario (the last `HttpResponseMessage`, a cached deserialized body) — not for the factory or client.

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

## Verifying how many times a downstream call happened

Every request WireMock receives lands in its request log — useful for asserting a downstream dependency was (or wasn't) called, e.g. checking a retry happened, or that enrichment only calls the directory once per booking. `Infrastructure/WireMockServerExtensions.cs` wraps the raw log query:

```csharp
public static int CallsTo(this WireMockServer server, string pathPrefix) =>
    server.LogEntries.Count(l => l.RequestMessage.Path.StartsWith(pathPrefix));
```

Use it in a step definition:

```csharp
_factory.DownstreamServers.Server.CallsTo("/customers/").Should().Be(1);
```

Only *received* requests show up here — stubs configured in a `Given` step don't count until something actually calls them. Since the server is shared and reset per scenario (see above), counts reflect only the current scenario's calls.

## Running the suite

```
dotnet test prepaid.api.BddTests/prepaid.api.BddTests.csproj
```

No real network calls and no real downstream service needs to be up — every dependency is served by one local WireMock instance, started once for the run and reset between scenarios.
