using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Contracts;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Orders;

public sealed class OrderQueryService(IDbContextFactory<OrderDbContext> contextFactory) : IOrderQueryService
{
    public async Task<OrderDetailsDto?> GetAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId && order.CustomerId == customerId)
            .Select(OrderProjection.Details)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == filter.CustomerId);

        if (filter.Status.HasValue)
        {
            query = query.Where(order => order.Status == filter.Status.Value);
        }

        if (filter.From.HasValue)
        {
            query = query.Where(order => order.CreatedAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(order => order.CreatedAt < filter.To.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(order => new OrderSummaryDto(
                order.Id,
                order.CustomerId,
                order.Status,
                order.Currency,
                order.Total,
                order.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return new PagedResult<OrderSummaryDto>(items, filter.Page, filter.PageSize, totalCount);
    }
}

