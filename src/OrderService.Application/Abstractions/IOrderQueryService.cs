using OrderService.Application.Contracts;

namespace OrderService.Application.Abstractions;

public interface IOrderQueryService
{
    Task<OrderDetailsDto?> GetAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);

    Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken);
}

