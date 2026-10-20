using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Catalog;
using OrderService.Domain.Orders;

namespace OrderService.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable(
            "order_items",
            table =>
            {
                table.HasCheckConstraint("ck_order_items_unit_price", "unit_price >= 0");
                table.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
            });
        builder.HasKey(item => new { item.OrderId, item.ProductId }).HasName("pk_order_items");
        builder.Property(item => item.OrderId).HasColumnName("order_id");
        builder.Property(item => item.ProductId).HasColumnName("product_id");
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("quantity").IsRequired();
        builder.Ignore(item => item.Subtotal);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_order_items_products_product_id");
        builder.HasIndex(item => item.ProductId).HasDatabaseName("ix_order_items_product_id");
    }
}

