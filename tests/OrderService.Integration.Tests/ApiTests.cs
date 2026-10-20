using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Contracts;
using Xunit;

namespace OrderService.Integration.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class ApiTests : IDisposable
{
    private static readonly Guid UserOne = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserTwo = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ProductOne = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ProductTwo = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid ProductThree = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private readonly PostgresFixture _database;
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(PostgresFixture database)
    {
        _database = database;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", database.ConnectionString);
            builder.UseSetting("Documentation:Enabled", "true");
            builder.UseSetting("InstanceId", "integration-test");
        });
    }

    [Fact]
    public async Task AuthenticationAndOwnershipAreEnforced()
    {
        using var owner = await CreateAuthenticatedClientAsync("user01");
        var (response, orderId) = await CreateOrderAsync(owner, UserOne, ProductOne, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        response.Dispose();

        using var anonymous = _factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync($"/orders/{orderId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var otherUser = await CreateAuthenticatedClientAsync("user02");
        using var hidden = await otherUser.GetAsync($"/orders/{orderId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task FullLifecycleAndCreationRetriesAreIdempotent()
    {
        using var client = await CreateAuthenticatedClientAsync("user01");
        var key = Guid.NewGuid();
        var (created, orderId) = await CreateOrderAsync(client, UserOne, ProductOne, key, quantity: 2);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        created.Dispose();

        var (replayed, replayedId) = await CreateOrderAsync(client, UserOne, ProductOne, key, quantity: 2);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.True(replayed.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(orderId, replayedId);
        replayed.Dispose();

        using var confirmed = await client.PostAsync($"/orders/{orderId}/confirm", null);
        using var confirmedAgain = await client.PostAsync($"/orders/{orderId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmedAgain.StatusCode);

        using var canceled = await client.PostAsync($"/orders/{orderId}/cancel", null);
        using var canceledAgain = await client.PostAsync($"/orders/{orderId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, canceledAgain.StatusCode);

        using var confirmCanceled = await client.PostAsync($"/orders/{orderId}/confirm", null);
        Assert.Equal(HttpStatusCode.Conflict, confirmCanceled.StatusCode);
    }

    [Fact]
    public async Task ConcurrentIdempotencyKeyCreatesOneOrder()
    {
        using var client = await CreateAuthenticatedClientAsync("user02");
        var key = Guid.NewGuid();

        var requests = Enumerable.Range(0, 20)
            .Select(_ => CreateOrderAsync(client, UserTwo, ProductThree, key))
            .ToArray();
        var results = await Task.WhenAll(requests);

        try
        {
            Assert.Single(results, result => result.Response.StatusCode == HttpStatusCode.Created);
            Assert.All(
                results,
                result => Assert.True(result.Response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
            Assert.Single(results.Select(result => result.OrderId).Distinct());
        }
        finally
        {
            foreach (var result in results)
            {
                result.Response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConcurrentConfirmationsNeverOversell()
    {
        await SetStockAsync(ProductTwo, 10);
        using var client = await CreateAuthenticatedClientAsync("user01");
        var orderIds = new List<Guid>();
        for (var index = 0; index < 20; index++)
        {
            var (response, orderId) = await CreateOrderAsync(client, UserOne, ProductTwo, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            response.Dispose();
            orderIds.Add(orderId);
        }

        var confirmations = await Task.WhenAll(orderIds.Select(id => client.PostAsync($"/orders/{id}/confirm", null)));
        try
        {
            Assert.Equal(10, confirmations.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(10, confirmations.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(0, await GetStockAsync(ProductTwo));
        }
        finally
        {
            foreach (var response in confirmations)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task PublicListRequiresCustomerAndReturnsOnlySummaries()
    {
        using var client = _factory.CreateClient();
        using var missingCustomer = await client.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.BadRequest, missingCustomer.StatusCode);

        using var response = await client.GetAsync($"/orders?customerId={UserOne}&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(UserOne, item.GetProperty("customerId").GetGuid());
            Assert.False(item.TryGetProperty("items", out _));
        }
    }

    [Fact]
    public async Task OpenApiScalarAndHealthAreAvailable()
    {
        using var client = _factory.CreateClient();
        using var openApi = await client.GetAsync("/openapi/v1.json");
        using var scalar = await client.GetAsync("/scalar");
        using var scalarBundle = await client.GetAsync("/scalar/scalar.js");
        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalarBundle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.True(openApi.Headers.Contains("X-Instance-Id"));
        var scalarHtml = await scalar.Content.ReadAsStringAsync();
        Assert.DoesNotContain("https://", scalarHtml, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        Assert.StartsWith("3.1", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(
            document.RootElement
                .GetProperty("components")
                .GetProperty("securitySchemes")
                .TryGetProperty("Bearer", out _));
        var parameters = document.RootElement
            .GetProperty("paths")
            .GetProperty("/orders")
            .GetProperty("post")
            .GetProperty("parameters")
            .EnumerateArray();
        Assert.Contains(parameters, parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.True(
            document.RootElement
                .GetProperty("paths")
                .GetProperty("/orders")
                .GetProperty("post")
                .TryGetProperty("security", out _));
        Assert.False(
            document.RootElement
                .GetProperty("paths")
                .GetProperty("/orders")
                .GetProperty("get")
                .TryGetProperty("security", out _));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string username)
    {
        var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/auth/token",
            new AuthRequest(username, "Demo@123456"));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static async Task<(HttpResponseMessage Response, Guid OrderId)> CreateOrderAsync(
        HttpClient client,
        Guid customerId,
        Guid productId,
        Guid idempotencyKey,
        int quantity = 1)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(
                customerId,
                "BRL",
                [new CreateOrderItemRequest(productId, quantity)]))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        var response = await client.SendAsync(request);
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            return (response, Guid.Empty);
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response, json.RootElement.GetProperty("id").GetGuid());
    }

    private async Task SetStockAsync(Guid productId, int quantity)
    {
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE products SET available_quantity = {quantity} WHERE id = {productId}");
    }

    private async Task<int> GetStockAsync(Guid productId)
    {
        await using var context = _database.CreateDbContext();
        return await context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.AvailableQuantity)
            .SingleAsync();
    }
}
