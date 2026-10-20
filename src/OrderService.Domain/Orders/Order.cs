using OrderService.Domain.Common;

namespace OrderService.Domain.Orders;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
        Currency = string.Empty;
        RequestHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string Currency { get; private set; }

    public decimal Total { get; private set; }

    public Guid IdempotencyKey { get; private set; }

    public string RequestHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public static DomainResult<Order> Place(
        Guid customerId,
        string currency,
        IReadOnlyCollection<OrderItemDraft> items,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset now)
    {
        if (customerId == Guid.Empty)
        {
            return DomainResult<Order>.Failure("customer_required", "Customer is required.", "customerId");
        }

        if (idempotencyKey == Guid.Empty)
        {
            return DomainResult<Order>.Failure("idempotency_key_invalid", "Idempotency key must be a non-empty UUID.");
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetter))
        {
            return DomainResult<Order>.Failure("currency_invalid", "Currency must be a three-letter ISO code.", "currency");
        }

        if (items.Count == 0)
        {
            return DomainResult<Order>.Failure("items_required", "An order must contain at least one item.", "items");
        }

        if (items.Any(item => item.ProductId == Guid.Empty))
        {
            return DomainResult<Order>.Failure("product_invalid", "Product id must be a non-empty UUID.", "items.productId");
        }

        if (items.Any(item => item.Quantity <= 0))
        {
            return DomainResult<Order>.Failure("quantity_invalid", "Item quantity must be greater than zero.", "items.quantity");
        }

        if (items.Any(item => item.UnitPrice < 0))
        {
            return DomainResult<Order>.Failure("unit_price_invalid", "Unit price cannot be negative.");
        }

        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count)
        {
            return DomainResult<Order>.Failure("duplicate_product", "Each product may appear only once.", "items");
        }

        var id = Guid.CreateVersion7(now);
        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            Status = OrderStatus.Placed,
            Currency = normalizedCurrency,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in items)
        {
            order._items.Add(new OrderItem(id, item.ProductId, item.UnitPrice, item.Quantity));
        }

        order.Total = order._items.Sum(item => item.Subtotal);
        return DomainResult<Order>.Success(order);
    }

    public DomainResult Confirm(DateTimeOffset now)
    {
        if (Status == OrderStatus.Confirmed)
        {
            return DomainResult.Success();
        }

        if (Status != OrderStatus.Placed)
        {
            return DomainResult.Failure("invalid_order_transition", $"An order in {Status} cannot be confirmed.");
        }

        Status = OrderStatus.Confirmed;
        UpdatedAt = now;
        return DomainResult.Success();
    }

    public DomainResult Cancel(DateTimeOffset now)
    {
        if (Status == OrderStatus.Canceled)
        {
            return DomainResult.Success();
        }

        if (Status is not (OrderStatus.Placed or OrderStatus.Confirmed))
        {
            return DomainResult.Failure("invalid_order_transition", $"An order in {Status} cannot be canceled.");
        }

        Status = OrderStatus.Canceled;
        UpdatedAt = now;
        return DomainResult.Success();
    }
}

