using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Users;

namespace OrderService.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id).HasName("pk_users");
        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
        builder.Property(user => user.NormalizedUsername).HasColumnName("normalized_username").HasMaxLength(100).IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(300).IsRequired();
        builder.Property(user => user.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(user => user.NormalizedUsername).IsUnique().HasDatabaseName("ux_users_normalized_username");
    }
}

