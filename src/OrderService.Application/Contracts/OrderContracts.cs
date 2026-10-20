using OrderService.Domain.Orders;

namespace OrderService.Application.Contracts;

public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);

public sealed record CreateOrderRequest(Guid CustomerId, string Currency, IReadOnlyCollection<CreateOrderItemRequest> Items);

public sealed record OrderItemDto(Guid ProductId, decimal UnitPrice, int Quantity, decimal Subtotal);

public sealed record OrderDetailsDto(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    string Currency,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<OrderItemDto> Items);

public sealed record OrderSummaryDto(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    string Currency,
    decimal Total,
    DateTimeOffset CreatedAt);

public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record OrderListFilter(
    Guid CustomerId,
    OrderStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public sealed record CreateOrderOutcome(OrderDetailsDto Order, bool Created);

public sealed record ProductSnapshot(Guid Id, string Currency, decimal UnitPrice, int AvailableQuantity);

public sealed record IdempotencyRecord(string RequestHash, OrderDetailsDto Order);

