using OrderService.Domain.Catalog;
using OrderService.Domain.Orders;
using OrderService.Domain.Users;
using Xunit;

namespace OrderService.Domain.Tests;

public sealed class OrderTests
{
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlaceCreatesPlacedOrderWithSnapshotAndTotal()
    {
        var result = Order.Place(
            CustomerId,
            "brl",
            [new OrderItemDraft(ProductId, 12.50m, 3)],
            Guid.NewGuid(),
            new string('a', 64),
            Now);

        Assert.True(result.IsSuccess);
        var order = Assert.IsType<Order>(result.Value);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal("BRL", order.Currency);
        Assert.Equal(37.50m, order.Total);
        var item = Assert.Single(order.Items);
        Assert.Equal(12.50m, item.UnitPrice);
        Assert.Equal(37.50m, item.Subtotal);
    }

    [Fact]
    public void PlaceRejectsEmptyOrder()
    {
        var result = Place([]);

        Assert.False(result.IsSuccess);
        Assert.Equal("items_required", result.Error?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PlaceRejectsNonPositiveQuantity(int quantity)
    {
        var result = Place([new OrderItemDraft(ProductId, 10m, quantity)]);

        Assert.False(result.IsSuccess);
        Assert.Equal("quantity_invalid", result.Error?.Code);
    }

    [Fact]
    public void PlaceRejectsDuplicateProduct()
    {
        var result = Place(
        [
            new OrderItemDraft(ProductId, 10m, 1),
            new OrderItemDraft(ProductId, 10m, 2)
        ]);

        Assert.False(result.IsSuccess);
        Assert.Equal("duplicate_product", result.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("123")]
    public void PlaceRejectsInvalidCurrency(string currency)
    {
        var result = Order.Place(
            CustomerId,
            currency,
            [new OrderItemDraft(ProductId, 10m, 1)],
            Guid.NewGuid(),
            new string('a', 64),
            Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("currency_invalid", result.Error?.Code);
    }

    [Fact]
    public void ConfirmIsIdempotent()
    {
        var order = Assert.IsType<Order>(Place([new OrderItemDraft(ProductId, 10m, 1)]).Value);

        var first = order.Confirm(Now.AddMinutes(1));
        var second = order.Confirm(Now.AddMinutes(2));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(Now.AddMinutes(1), order.UpdatedAt);
    }

    [Fact]
    public void CancelConfirmedOrderIsIdempotentAndCannotBeConfirmedAgain()
    {
        var order = Assert.IsType<Order>(Place([new OrderItemDraft(ProductId, 10m, 1)]).Value);
        order.Confirm(Now.AddMinutes(1));

        var first = order.Cancel(Now.AddMinutes(2));
        var second = order.Cancel(Now.AddMinutes(3));
        var confirm = order.Confirm(Now.AddMinutes(4));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(confirm.IsSuccess);
        Assert.Equal("invalid_order_transition", confirm.Error?.Code);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(Now.AddMinutes(2), order.UpdatedAt);
    }

    [Fact]
    public void PlaceRejectsInvalidIdentifiersAndPrice()
    {
        var missingCustomer = Order.Place(
            Guid.Empty,
            "BRL",
            [new OrderItemDraft(ProductId, 10m, 1)],
            Guid.NewGuid(),
            new string('a', 64),
            Now);
        var missingKey = Order.Place(
            CustomerId,
            "BRL",
            [new OrderItemDraft(ProductId, 10m, 1)],
            Guid.Empty,
            new string('a', 64),
            Now);
        var missingProduct = Place([new OrderItemDraft(Guid.Empty, 10m, 1)]);
        var negativePrice = Place([new OrderItemDraft(ProductId, -0.01m, 1)]);

        Assert.Equal("customer_required", missingCustomer.Error?.Code);
        Assert.Equal("idempotency_key_invalid", missingKey.Error?.Code);
        Assert.Equal("product_invalid", missingProduct.Error?.Code);
        Assert.Equal("unit_price_invalid", negativePrice.Error?.Code);
    }

    [Fact]
    public void CancelPlacedOrderSucceeds()
    {
        var order = Assert.IsType<Order>(Place([new OrderItemDraft(ProductId, 10m, 1)]).Value);

        var result = order.Cancel(Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
    }

    [Fact]
    public void ProductAndUserNormalizeTheirValues()
    {
        var product = new Product(ProductId, "SKU", "Product", "brl", 10m, 2, Now);
        var user = new User(CustomerId, " user01 ", "hash", Now);

        Assert.Equal("BRL", product.Currency);
        Assert.Equal("USER01", user.NormalizedUsername);
        Assert.True(user.IsActive);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Product(ProductId, "SKU", "Product", "BRL", -1m, 2, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Product(ProductId, "SKU", "Product", "BRL", 1m, -1, Now));
    }

    private static OrderService.Domain.Common.DomainResult<Order> Place(IReadOnlyCollection<OrderItemDraft> items) =>
        Order.Place(CustomerId, "BRL", items, Guid.NewGuid(), new string('a', 64), Now);
}
