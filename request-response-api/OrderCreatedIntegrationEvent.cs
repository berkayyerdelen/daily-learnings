using MassTransit;

namespace request_response_api;

public class OrderCreatedIntegrationEvent
{
    public Guid UniqueId { get; set; } = Guid.NewGuid();
}

public class OrderCreatedIntegrationEventHandler : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var response = new OrderCreatedIntegrationEventHandlerResponse
        {
            UniqueId = context.Message.UniqueId,
            Result = "Order created successfully"
        };
        await context.RespondAsync(response);
    }
}

public class OrderCreatedIntegrationEventHandlerResponse
{
    public Guid UniqueId { get; set; }
    public string Result { get; set; }
}