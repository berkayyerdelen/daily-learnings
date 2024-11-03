using MassTransit;
using MassTransit.Clients;
using Microsoft.AspNetCore.Http.HttpResults;
using request_response_api;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedIntegrationEventHandler>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ReceiveEndpoint("order-created", e =>
        {
            e.ConfigureConsumer<OrderCreatedIntegrationEventHandler>(context);
        });
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();



app.MapGet("/order", async (IRequestClient<OrderCreatedIntegrationEvent> orderCreatedIntegrationEventClient) =>
    {
        // Send a request and wait for the response
        var response = await orderCreatedIntegrationEventClient.GetResponse<OrderCreatedIntegrationEventHandlerResponse>(new OrderCreatedIntegrationEvent
        {
            UniqueId = Guid.NewGuid()
        });
    
        return Results.Ok(response.Message.Result);
    })
    .WithName("CreateOrder")
    .WithOpenApi();


app.Run();
