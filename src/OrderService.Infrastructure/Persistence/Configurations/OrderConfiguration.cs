using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Orders;
using OrderService.Domain.Users;

namespace OrderService.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable(
            "orders",
            table =>
            {
                table.HasCheckConstraint("ck_orders_total", "total >= 0");
                table.HasCheckConstraint("ck_orders_currency", "length(currency) = 3");
                table.HasCheckConstraint("ck_orders_request_hash", "length(request_hash) = 64");
                table.HasCheckConstraint("ck_orders_status", "status IN (0, 1, 2, 3)");
            });
        builder.HasKey(order => order.Id).HasName("pk_orders");
        builder.Property(order => order.Id).HasColumnName("id");
        builder.Property(order => order.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(order => order.Status).HasColumnName("status").HasConversion<short>().IsRequired();
        builder.Property(order => order.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(order => order.Total).HasColumnName("total").HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
        builder.Property(order => order.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(order => order.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(order => order.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_orders_users_customer_id");

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_order_items_orders_order_id");
        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(order => new { order.CustomerId, order.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_orders_customer_idempotency_key");
        builder.HasIndex(order => new { order.CreatedAt, order.Id })
            .IsDescending()
            .HasDatabaseName("ix_orders_created_at_id");
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAt, order.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_orders_customer_created_at_id");
        builder.HasIndex(order => new { order.CustomerId, order.Status, order.CreatedAt, order.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("ix_orders_customer_status_created_at_id");
    }
}

