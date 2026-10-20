using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrderService.Integration.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orders_tests")
        .WithUsername("orders")
        .WithPassword("orders")
        .WithCleanUp(false)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(context, DatabaseSeeder.DefaultDemoPassword);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public OrderDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new OrderDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration";
}
