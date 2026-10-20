using System.Linq.Expressions;
using OrderService.Application.Contracts;
using OrderService.Domain.Orders;

namespace OrderService.Infrastructure.Orders;

internal static class OrderProjection
{
    public static Expression<Func<Order, OrderDetailsDto>> Details => order => new OrderDetailsDto(
        order.Id,
        order.CustomerId,
        order.Status,
        order.Currency,
        order.Total,
        order.CreatedAt,
        order.UpdatedAt,
        order.Items
            .OrderBy(item => item.ProductId)
            .Select(item => new OrderItemDto(item.ProductId, item.UnitPrice, item.Quantity, item.UnitPrice * item.Quantity))
            .ToArray());

    public static OrderDetailsDto Map(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status,
        order.Currency,
        order.Total,
        order.CreatedAt,
        order.UpdatedAt,
        order.Items
            .OrderBy(item => item.ProductId)
            .Select(item => new OrderItemDto(item.ProductId, item.UnitPrice, item.Quantity, item.Subtotal))
            .ToArray());
}

