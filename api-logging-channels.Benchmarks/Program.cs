// Measures what ApiLogFilter adds to one request: time and bytes allocated.
//
//   dotnet run -c Release --project api-logging-channels.Benchmarks
//
// "NoLogging" executes the MVC JSON result exactly as the framework would; "WithLogging" runs the
// same thing wrapped by ApiLogFilter. The difference is the filter's cost per request.

using System.Threading.Channels;
using ApiLogging.Api.Controllers;
using ApiLogging.Api.Logging;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

BenchmarkRunner.Run<FilterBenchmarks>(
    DefaultConfig.Instance.AddJob(Job.MediumRun.WithToolchain(InProcessEmitToolchain.Instance)), args);

public enum ResponseShape
{
    /// <summary>One small object with a body-sized string.</summary>
    Single,
    /// <summary>A list of 50 orders: a typical "GET collection" response, costly to serialize.</summary>
    List50
}

[MemoryDiagnoser]
public class FilterBenchmarks
{
    [Params(100, 10_000)]
    public int BodySize;

    [Params(ResponseShape.Single, ResponseShape.List50)]
    public ResponseShape Shape;

    private IServiceProvider _services = null!;
    private IAsyncActionFilter _filter = null!;
    private ControllerActionDescriptor _action = null!;
    private Endpoint _endpoint = null!;
    private CreateOrderRequest _request = null!;
    private object _response = null!;
    private Task _drain = null!;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:ApiLogs"] = "Server=unused" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        services.AddApiLogging(configuration);
        _services = services.BuildServiceProvider();
        _filter = _services.GetRequiredService<ApiLogFilter>();

        // Typical controller action metadata (what [ApiController] + routing attributes produce).
        var metadata = new List<object>
        {
            new ApiLogAttribute(), // opt in, as a logged endpoint would
            new ApiControllerAttribute(), new RouteAttribute("api/orders"), new HttpPostAttribute(),
            new ProducesAttribute("application/json"), new ConsumesAttribute("application/json")
        };
        _action = new ControllerActionDescriptor
        {
            ControllerName = "Orders",
            ActionName = "Create",
            EndpointMetadata = metadata,
            Parameters =
            [
                new ParameterDescriptor
                {
                    Name = "request",
                    ParameterType = typeof(CreateOrderRequest),
                    BindingInfo = new BindingInfo { BindingSource = BindingSource.Body }
                }
            ]
        };
        _endpoint = new Endpoint(null, new EndpointMetadataCollection(metadata), "Orders.Create");

        _request = new CreateOrderRequest("c-42", 10.5m, "4111", new string('x', BodySize));
        _response = Shape == ResponseShape.Single
            ? new OrderResponse(Guid.NewGuid(), "c-42" + new string('y', BodySize), 10.5m, DateTimeOffset.UtcNow)
            : Enumerable.Range(0, 50)
                .Select(i => new OrderResponse(Guid.NewGuid(), $"c-{i}" + new string('y', BodySize / 50), i, DateTimeOffset.UtcNow))
                .ToList();

        // Stand-in for ApiLogBackgroundService: keep the channel empty and release the memory budget.
        var reader = _services.GetRequiredService<ChannelReader<ApiLog>>();
        var budget = _services.GetRequiredService<ApiLogMemoryBudget>();
        _drain = Task.Run(async () =>
        {
            while (await reader.WaitToReadAsync())
                while (reader.TryRead(out var log))
                    budget.Release(log.ApproximateBytes);
        });
    }

    [Benchmark(Baseline = true)]
    public Task NoLogging()
    {
        var (actionContext, result) = NewRequest();
        return result.ExecuteResultAsync(actionContext);
    }

    [Benchmark]
    public async Task WithLogging()
    {
        var (actionContext, result) = NewRequest();
        var filters = new List<IFilterMetadata>();

        var executing = new ActionExecutingContext(actionContext, filters,
            new Dictionary<string, object?> { ["request"] = _request }, controller: null!);
        await _filter.OnActionExecutionAsync(executing,
            () => Task.FromResult(new ActionExecutedContext(actionContext, filters, controller: null!) { Result = result }));

        // MVC runs result filters around result execution; without one, it just executes the result.
        if (_filter is IAsyncResultFilter resultFilter)
        {
            var resultExecuting = new ResultExecutingContext(actionContext, filters, result, controller: null!);
            await resultFilter.OnResultExecutionAsync(resultExecuting, async () =>
            {
                await result.ExecuteResultAsync(actionContext);
                return new ResultExecutedContext(actionContext, filters, result, controller: null!);
            });
        }
        else
        {
            await result.ExecuteResultAsync(actionContext);
        }
    }

    private (ActionContext, ObjectResult) NewRequest()
    {
        var http = new DefaultHttpContext { RequestServices = _services };
        http.Request.Method = "POST";
        http.Request.Path = "/api/orders";
        http.Response.Body = Stream.Null;
        http.SetEndpoint(_endpoint);
        return (new ActionContext(http, new RouteData(), _action), new ObjectResult(_response) { StatusCode = 201 });
    }
}
