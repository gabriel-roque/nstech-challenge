using OrderService.Application.Common;
using OrderService.Application.Contracts;
using OrderService.Domain.Orders;

namespace OrderService.Application.Abstractions;

public interface IOrderStore
{
    Task<IdempotencyRecord?> FindByIdempotencyKeyAsync(Guid customerId, Guid key, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ProductSnapshot>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    Task<AppResult<CreateOrderOutcome>> CreateAsync(Order order, CancellationToken cancellationToken);

    Task<AppResult<OrderDetailsDto>> ConfirmAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);

    Task<AppResult<OrderDetailsDto>> CancelAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);
}

