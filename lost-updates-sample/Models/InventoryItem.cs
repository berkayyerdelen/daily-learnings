namespace LostUpdates.Api.Models;

public sealed class InventoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
}
