using MassTransit;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using saga.events;
using saga.events.saga;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

BsonSerializer.RegisterSerializer(new MongoDB.Bson.Serialization.Serializers.GuidSerializer(GuidRepresentation.Standard));

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumers(typeof(Program).Assembly);
    
    x.AddSagaStateMachine<OrderSaga, OrderSagaData>()
        .MongoDbRepository(r =>
        {
            r.Connection = "mongodb://localhost:27017";  // MongoDB connection string
            r.DatabaseName = "sagaDb";                   // Database name
            r.CollectionName = "orderSagas";             // Collection name
            
        });
    
    
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(context);
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

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapPost("/order-created", async ([FromQuery] Guid orderId, [FromServices] IBus publishEndpoint) =>
{
    await publishEndpoint.Publish(new OrderCreatedEvent()
    {
        OrderId = orderId,
        Email = "berkay@gmail.com"
    });
    return Results.Accepted();
});

app.MapPost("/invoice-created", async ([FromQuery] Guid orderId, [FromServices] IBus publishEndpoint) =>
{
    await publishEndpoint.Publish(new InvoiceCreatedEvent()
    {
        OrderId = orderId,
        InvoiceId = Guid.NewGuid(),
    });
    return Results.Accepted();
});

app.Run();


