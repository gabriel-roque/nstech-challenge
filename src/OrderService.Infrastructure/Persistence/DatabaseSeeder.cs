using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Catalog;
using OrderService.Domain.Users;
using OrderService.Infrastructure.Auth;

namespace OrderService.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public const string DefaultDemoPassword = "Demo@123456";

    public static async Task SeedAsync(
        OrderDbContext context,
        string demoPassword,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existingUserIds = await context.Users.AsNoTracking().Select(user => user.Id).ToHashSetAsync(cancellationToken);
        for (var index = 1; index <= 10; index++)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}");
            if (!existingUserIds.Contains(id))
            {
                var username = $"user{index:00}";
                context.Users.Add(new User(id, username, PasswordHasher.Hash(demoPassword), now));
            }
        }

        var existingProductIds = await context.Products.AsNoTracking().Select(product => product.Id).ToHashSetAsync(cancellationToken);
        for (var index = 1; index <= 10; index++)
        {
            var id = Guid.Parse($"10000000-0000-0000-0000-{index:000000000000}");
            if (!existingProductIds.Contains(id))
            {
                context.Products.Add(new Product(
                    id,
                    $"SKU-{index:000}",
                    $"Demo product {index:00}",
                    "BRL",
                    10.00m * index,
                    100,
                    now));
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
