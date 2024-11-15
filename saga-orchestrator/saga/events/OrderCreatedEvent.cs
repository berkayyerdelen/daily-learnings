using MassTransit;

namespace saga.events;

public class OrderCreatedEvent
{
    public Guid OrderId { get; set; }
    public string? Email { get; set; }
}

public class OrderCreatedEventHandler: IConsumer<OrderCreatedEvent>
{
    public Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        Console.WriteLine($"Order created: {context.Message.OrderId}");
        return Task.CompletedTask;
    }
}

public class InvoiceCreatedEvent
{
    public Guid OrderId { get; set; }
    public Guid InvoiceId { get; set; }
}

public class InvoiceCreatedEventHandler: IConsumer<InvoiceCreatedEvent>
{
    public Task Consume(ConsumeContext<InvoiceCreatedEvent> context)
    {
        Console.WriteLine($"Invoice created: {context.Message.InvoiceId}");
        return Task.CompletedTask;
    }
}

public class SendConfirmationCommand
{
    public string Message { get; set; }
    public string Email { get; set; }
}

