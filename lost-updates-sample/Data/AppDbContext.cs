using LostUpdates.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LostUpdates.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Quantity).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_inventory_items_quantity_nonnegative", "quantity >= 0"));
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Address).HasMaxLength(500).IsRequired();
            entity.Property(x => x.DeliveryInstructions).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
    }
}
