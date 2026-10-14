namespace LostUpdates.Api.Models;

public sealed class Order
{
    public int Id { get; set; }
    public string Address { get; set; } = "";
    public string DeliveryInstructions { get; set; } = "";
    // Application-managed optimistic concurrency token.
    public int Version { get; set; } = 1;
}
