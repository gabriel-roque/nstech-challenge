using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Contracts;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Orders;

public sealed class OrderStore(IDbContextFactory<OrderDbContext> contextFactory, IClock clock) : IOrderStore
{
    private const int MaxTransactionAttempts = 3;

    public async Task<IdempotencyRecord?> FindByIdempotencyKeyAsync(
        Guid customerId,
        Guid key,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var order = await context.Orders
            .AsNoTracking()
            .Where(candidate => candidate.CustomerId == customerId && candidate.IdempotencyKey == key)
            .Select(candidate => new
            {
                candidate.RequestHash,
                Details = new OrderDetailsDto(
                    candidate.Id,
                    candidate.CustomerId,
                    candidate.Status,
                    candidate.Currency,
                    candidate.Total,
                    candidate.CreatedAt,
                    candidate.UpdatedAt,
                    candidate.Items
                        .OrderBy(item => item.ProductId)
                        .Select(item => new OrderItemDto(
                            item.ProductId,
                            item.UnitPrice,
                            item.Quantity,
                            item.UnitPrice * item.Quantity))
                        .ToArray())
            })
            .SingleOrDefaultAsync(cancellationToken);

        return order is null ? null : new IdempotencyRecord(order.RequestHash, order.Details);
    }

    public async Task<IReadOnlyDictionary<Guid, ProductSnapshot>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new ProductSnapshot(product.Id, product.Currency, product.UnitPrice, product.AvailableQuantity))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
    }

    public async Task<AppResult<CreateOrderOutcome>> CreateAsync(Order order, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Orders.Add(order);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return AppResult<CreateOrderOutcome>.Success(new CreateOrderOutcome(OrderProjection.Map(order), true));
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            await using var winnerContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var winner = await winnerContext.Orders
                .AsNoTracking()
                .Where(candidate =>
                    candidate.CustomerId == order.CustomerId &&
                    candidate.IdempotencyKey == order.IdempotencyKey)
                .Select(candidate => new
                {
                    candidate.RequestHash,
                    Details = new OrderDetailsDto(
                        candidate.Id,
                        candidate.CustomerId,
                        candidate.Status,
                        candidate.Currency,
                        candidate.Total,
                        candidate.CreatedAt,
                        candidate.UpdatedAt,
                        candidate.Items
                            .OrderBy(item => item.ProductId)
                            .Select(item => new OrderItemDto(
                                item.ProductId,
                                item.UnitPrice,
                                item.Quantity,
                                item.UnitPrice * item.Quantity))
                            .ToArray())
                })
                .SingleAsync(cancellationToken);

            return winner.RequestHash == order.RequestHash
                ? AppResult<CreateOrderOutcome>.Success(new CreateOrderOutcome(winner.Details, false))
                : AppResult<CreateOrderOutcome>.Failure(
                    ErrorType.Conflict,
                    "idempotency_key_reused",
                    "The idempotency key was already used with a different payload.");
        }
    }

    public Task<AppResult<OrderDetailsDto>> ConfirmAsync(
        Guid orderId,
        Guid customerId,
        CancellationToken cancellationToken) =>
        ExecuteWithDeadlockRetryAsync(
            context => ConfirmOnceAsync(context, orderId, customerId, cancellationToken),
            cancellationToken);

    public Task<AppResult<OrderDetailsDto>> CancelAsync(
        Guid orderId,
        Guid customerId,
        CancellationToken cancellationToken) =>
        ExecuteWithDeadlockRetryAsync(
            context => CancelOnceAsync(context, orderId, customerId, cancellationToken),
            cancellationToken);

    private async Task<AppResult<OrderDetailsDto>> ConfirmOnceAsync(
        OrderDbContext context,
        Guid orderId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var order = await LoadForUpdateAsync(context, orderId, customerId, cancellationToken);
        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AppResult<OrderDetailsDto>.Failure(ErrorType.NotFound, "order_not_found", "Order was not found.");
        }

        if (order.Status == OrderStatus.Confirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return AppResult<OrderDetailsDto>.Success(OrderProjection.Map(order));
        }

        if (order.Status != OrderStatus.Placed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AppResult<OrderDetailsDto>.Failure(
                ErrorType.Conflict,
                "invalid_order_transition",
                $"An order in {order.Status} cannot be confirmed.");
        }

        var now = clock.UtcNow;
        foreach (var item in order.Items.OrderBy(item => item.ProductId))
        {
            var affected = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE products
                SET available_quantity = available_quantity - {item.Quantity}, updated_at = {now}
                WHERE id = {item.ProductId} AND available_quantity >= {item.Quantity}
                """,
                cancellationToken);
            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AppResult<OrderDetailsDto>.Failure(
                    ErrorType.Conflict,
                    "insufficient_stock",
                    $"Product '{item.ProductId}' does not have enough stock.");
            }
        }

        order.Confirm(now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AppResult<OrderDetailsDto>.Success(OrderProjection.Map(order));
    }

    private async Task<AppResult<OrderDetailsDto>> CancelOnceAsync(
        OrderDbContext context,
        Guid orderId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var order = await LoadForUpdateAsync(context, orderId, customerId, cancellationToken);
        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AppResult<OrderDetailsDto>.Failure(ErrorType.NotFound, "order_not_found", "Order was not found.");
        }

        if (order.Status == OrderStatus.Canceled)
        {
            await transaction.CommitAsync(cancellationToken);
            return AppResult<OrderDetailsDto>.Success(OrderProjection.Map(order));
        }

        var shouldRestoreStock = order.Status == OrderStatus.Confirmed;
        if (order.Status is not (OrderStatus.Placed or OrderStatus.Confirmed))
        {
            await transaction.RollbackAsync(cancellationToken);
            return AppResult<OrderDetailsDto>.Failure(
                ErrorType.Conflict,
                "invalid_order_transition",
                $"An order in {order.Status} cannot be canceled.");
        }

        var now = clock.UtcNow;
        if (shouldRestoreStock)
        {
            foreach (var item in order.Items.OrderBy(item => item.ProductId))
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE products
                    SET available_quantity = available_quantity + {item.Quantity}, updated_at = {now}
                    WHERE id = {item.ProductId}
                    """,
                    cancellationToken);
            }
        }

        order.Cancel(now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AppResult<OrderDetailsDto>.Success(OrderProjection.Map(order));
    }

    private static Task<Order?> LoadForUpdateAsync(
        OrderDbContext context,
        Guid orderId,
        Guid customerId,
        CancellationToken cancellationToken) =>
        context.Orders
            .FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} AND customer_id = {customerId} FOR UPDATE")
            .Include(order => order.Items)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<AppResult<OrderDetailsDto>> ExecuteWithDeadlockRetryAsync(
        Func<OrderDbContext, Task<AppResult<OrderDetailsDto>>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTransactionAttempts; attempt++)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
                return await operation(context);
            }
            catch (Exception exception) when (IsDeadlock(exception) && attempt < MaxTransactionAttempts)
            {
                await Task.Delay(Random.Shared.Next(10, 51) * attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException("The transaction retry loop ended unexpectedly.");
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_orders_customer_idempotency_key"
        };

    private static bool IsDeadlock(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } ||
        exception.InnerException is not null && IsDeadlock(exception.InnerException);
}

