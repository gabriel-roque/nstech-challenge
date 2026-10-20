using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Catalog;

namespace OrderService.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable(
            "products",
            table =>
            {
                table.HasCheckConstraint("ck_products_unit_price", "unit_price >= 0");
                table.HasCheckConstraint("ck_products_available_quantity", "available_quantity >= 0");
                table.HasCheckConstraint("ck_products_currency", "length(currency) = 3");
            });
        builder.HasKey(product => product.Id).HasName("pk_products");
        builder.Property(product => product.Id).HasColumnName("id");
        builder.Property(product => product.Sku).HasColumnName("sku").HasMaxLength(50).IsRequired();
        builder.Property(product => product.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(product => product.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(product => product.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 2).IsRequired();
        builder.Property(product => product.AvailableQuantity).HasColumnName("available_quantity").IsRequired();
        builder.Property(product => product.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(product => product.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(product => product.Sku).IsUnique().HasDatabaseName("ux_products_sku");
    }
}

