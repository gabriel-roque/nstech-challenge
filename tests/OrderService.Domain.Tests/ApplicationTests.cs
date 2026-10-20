using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Contracts;
using OrderService.Application.Orders;
using OrderService.Domain.Orders;
using Xunit;

namespace OrderService.Domain.Tests;

public sealed class ApplicationTests
{
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ProductOne = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ProductTwo = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [Fact]
    public void FingerprintIsIndependentOfItemOrderAndCurrencyCase()
    {
        var first = new CreateOrderRequest(
            CustomerId,
            "brl",
            [new CreateOrderItemRequest(ProductTwo, 2), new CreateOrderItemRequest(ProductOne, 1)]);
        var second = new CreateOrderRequest(
            CustomerId,
            "BRL",
            [new CreateOrderItemRequest(ProductOne, 1), new CreateOrderItemRequest(ProductTwo, 2)]);

        Assert.Equal(RequestFingerprint.Compute(first), RequestFingerprint.Compute(second));
    }

    [Fact]
    public async Task CreateUsesCatalogPriceAndPersistsValidOrder()
    {
        var store = new FakeOrderStore(
            new ProductSnapshot(ProductOne, "BRL", 19.90m, 10));
        var handler = new CreateOrderHandler(store, new FakeClock());
        var request = new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 2)]);

        var result = await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(39.80m, result.Value?.Order.Total);
        Assert.NotNull(store.CreatedOrder);
    }

    [Fact]
    public async Task CreateRejectsMissingProduct()
    {
        var handler = new CreateOrderHandler(new FakeOrderStore(), new FakeClock());
        var request = new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 1)]);

        var result = await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("product_not_found", result.Error?.Code);
    }

    [Fact]
    public async Task CreateRejectsInsufficientStock()
    {
        var handler = new CreateOrderHandler(
            new FakeOrderStore(new ProductSnapshot(ProductOne, "BRL", 10m, 1)),
            new FakeClock());
        var request = new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 2)]);

        var result = await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("insufficient_stock", result.Error?.Code);
    }

    [Fact]
    public async Task CreateReturnsExistingOrderForSameKeyAndHash()
    {
        var key = Guid.NewGuid();
        var request = new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 1)]);
        var existing = CreateDetails();
        var store = new FakeOrderStore
        {
            Existing = new IdempotencyRecord(RequestFingerprint.Compute(request), existing)
        };
        var handler = new CreateOrderHandler(store, new FakeClock());

        var result = await handler.HandleAsync(request, key, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value?.Created);
        Assert.Equal(existing.Id, result.Value?.Order.Id);
        Assert.Null(store.CreatedOrder);
    }

    [Fact]
    public async Task CreateRejectsReusedKeyWithDifferentPayload()
    {
        var request = new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 1)]);
        var store = new FakeOrderStore { Existing = new IdempotencyRecord(new string('f', 64), CreateDetails()) };
        var handler = new CreateOrderHandler(store, new FakeClock());

        var result = await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("idempotency_key_reused", result.Error?.Code);
    }

    [Fact]
    public async Task CreateValidatesRequestBeforeCatalogAccess()
    {
        var handler = new CreateOrderHandler(new FakeOrderStore(), new FakeClock());
        var empty = await handler.HandleAsync(
            new CreateOrderRequest(CustomerId, "BRL", []),
            Guid.NewGuid(),
            CancellationToken.None);
        var quantity = await handler.HandleAsync(
            new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 0)]),
            Guid.NewGuid(),
            CancellationToken.None);
        var duplicate = await handler.HandleAsync(
            new CreateOrderRequest(
                CustomerId,
                "BRL",
                [new CreateOrderItemRequest(ProductOne, 1), new CreateOrderItemRequest(ProductOne, 2)]),
            Guid.NewGuid(),
            CancellationToken.None);
        var key = await handler.HandleAsync(
            new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 1)]),
            Guid.Empty,
            CancellationToken.None);

        Assert.Equal("items_required", empty.Error?.Code);
        Assert.Equal("quantity_invalid", quantity.Error?.Code);
        Assert.Equal("duplicate_product", duplicate.Error?.Code);
        Assert.Equal("idempotency_key_invalid", key.Error?.Code);
    }

    [Fact]
    public async Task CreateRejectsCurrencyMismatch()
    {
        var handler = new CreateOrderHandler(
            new FakeOrderStore(new ProductSnapshot(ProductOne, "USD", 10m, 10)),
            new FakeClock());

        var result = await handler.HandleAsync(
            new CreateOrderRequest(CustomerId, "BRL", [new CreateOrderItemRequest(ProductOne, 1)]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("currency_mismatch", result.Error?.Code);
    }

    [Fact]
    public void PagedResultCalculatesTotalPages()
    {
        var empty = new PagedResult<int>([], 1, 20, 0);
        var populated = new PagedResult<int>([1], 1, 20, 21);

        Assert.Equal(0, empty.TotalPages);
        Assert.Equal(2, populated.TotalPages);
    }

    private static OrderDetailsDto CreateDetails() => new(
        Guid.NewGuid(),
        CustomerId,
        OrderStatus.Placed,
        "BRL",
        10m,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        [new OrderItemDto(ProductOne, 10m, 1, 10m)]);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeOrderStore(params ProductSnapshot[] products) : IOrderStore
    {
        private readonly IReadOnlyDictionary<Guid, ProductSnapshot> _products = products.ToDictionary(product => product.Id);

        public IdempotencyRecord? Existing { get; init; }

        public Order? CreatedOrder { get; private set; }

        public Task<IdempotencyRecord?> FindByIdempotencyKeyAsync(
            Guid customerId,
            Guid key,
            CancellationToken cancellationToken) => Task.FromResult(Existing);

        public Task<IReadOnlyDictionary<Guid, ProductSnapshot>> GetProductsAsync(
            IReadOnlyCollection<Guid> productIds,
            CancellationToken cancellationToken) => Task.FromResult(_products);

        public Task<AppResult<CreateOrderOutcome>> CreateAsync(Order order, CancellationToken cancellationToken)
        {
            CreatedOrder = order;
            var details = new OrderDetailsDto(
                order.Id,
                order.CustomerId,
                order.Status,
                order.Currency,
                order.Total,
                order.CreatedAt,
                order.UpdatedAt,
                order.Items.Select(item => new OrderItemDto(item.ProductId, item.UnitPrice, item.Quantity, item.Subtotal)).ToArray());
            return Task.FromResult(AppResult<CreateOrderOutcome>.Success(new CreateOrderOutcome(details, true)));
        }

        public Task<AppResult<OrderDetailsDto>> ConfirmAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AppResult<OrderDetailsDto>> CancelAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
