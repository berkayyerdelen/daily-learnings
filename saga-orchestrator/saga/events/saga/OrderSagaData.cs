using MassTransit;
using MongoDB.Bson.Serialization.Attributes;

namespace saga.events.saga;

public class OrderSagaData : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }
    public Guid OrderId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsOrderCreated { get; set; }
    public bool IsInvoiceGenerated { get; set; }
    public bool IsEmailSend { get; set; }
    public int Version { get; set; }
}

public class OrderSaga : MassTransitStateMachine<OrderSagaData>
{
    public State InvoiceCreated { get; set; }
    public State OrderCreated { get; set; }

    public Event<InvoiceCreatedEvent> InvoiceCreatedEvent { get; set; }
    public Event<OrderCreatedEvent> OrderCreatedEvent { get; set; }


    public OrderSaga()
    {
        InstanceState(x=>x.CurrentState);
        Event(()=>OrderCreatedEvent, x=>x.CorrelateById(context=>context.Message.OrderId));
        Event(()=>InvoiceCreatedEvent, x=>x.CorrelateById(context=>context.Message.OrderId));

        Initially(
            When(OrderCreatedEvent)
                .Then(context =>
                {
                    context.Instance.OrderId = context.Data.OrderId;
                    context.Instance.Email = context.Data.Email;
                    context.Instance.IsOrderCreated = true;

                    Console.WriteLine($"Saga started for OrderId: {context.Data.OrderId}");
                })
                .TransitionTo(OrderCreated)
        );
        
        During(OrderCreated,
            When(InvoiceCreatedEvent)
                .Then(context =>
                {
                    context.Instance.IsInvoiceGenerated = true;

                    Console.WriteLine($"Invoice created for OrderId: {context.Instance.OrderId}");
                })
                .TransitionTo(InvoiceCreated)
        );
        
        During(InvoiceCreated,
            When(InvoiceCreatedEvent) // You can add more logic or a send confirmation step here
                .Then(context =>
                {
                    context.Instance.IsEmailSend = true;

                    Console.WriteLine($"Email sent for OrderId: {context.Instance.OrderId}");
                })
        );
            
    }
}