using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Persistence;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? "Host=localhost;Port=5432;Database=orders;Username=orders;Password=orders";
var demoPassword = Environment.GetEnvironmentVariable("Seed__DemoPassword")
    ?? DatabaseSeeder.DefaultDemoPassword;
var options = new DbContextOptionsBuilder<OrderDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(OrderDbContext).Assembly.FullName))
    .Options;

await using var context = new OrderDbContext(options);
if (!args.Contains("--seed-only", StringComparer.Ordinal))
{
    await context.Database.MigrateAsync();
}

await DatabaseSeeder.SeedAsync(context, demoPassword);
Console.WriteLine("Database migration and idempotent seed completed.");
