using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Contracts;

namespace OrderService.Application.Orders;

public sealed class ConfirmOrderHandler(IOrderStore store)
{
    public Task<AppResult<OrderDetailsDto>> HandleAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
        store.ConfirmAsync(orderId, customerId, cancellationToken);
}

