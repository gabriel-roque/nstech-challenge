using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Contracts;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders;

public sealed class CreateOrderHandler(IOrderStore store, IClock clock)
{
    public async Task<AppResult<CreateOrderOutcome>> HandleAsync(
        CreateOrderRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey == Guid.Empty)
        {
            return AppResult<CreateOrderOutcome>.Failure(
                ErrorType.Validation,
                "idempotency_key_invalid",
                "Idempotency-Key must be a non-empty UUID.",
                "Idempotency-Key");
        }

        if (request.Items.Count == 0)
        {
            return AppResult<CreateOrderOutcome>.Failure(ErrorType.Validation, "items_required", "At least one item is required.", "items");
        }

        if (request.Items.Any(item => item.Quantity <= 0))
        {
            return AppResult<CreateOrderOutcome>.Failure(ErrorType.Validation, "quantity_invalid", "Quantity must be greater than zero.", "items.quantity");
        }

        if (request.Items.Select(item => item.ProductId).Distinct().Count() != request.Items.Count)
        {
            return AppResult<CreateOrderOutcome>.Failure(ErrorType.Validation, "duplicate_product", "Each product may appear only once.", "items");
        }

        var hash = RequestFingerprint.Compute(request);
        var existing = await store.FindByIdempotencyKeyAsync(request.CustomerId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == hash
                ? AppResult<CreateOrderOutcome>.Success(new CreateOrderOutcome(existing.Order, false))
                : AppResult<CreateOrderOutcome>.Failure(
                    ErrorType.Conflict,
                    "idempotency_key_reused",
                    "The idempotency key was already used with a different payload.");
        }

        var productIds = request.Items.Select(item => item.ProductId).ToArray();
        var products = await store.GetProductsAsync(productIds, cancellationToken);
        var normalizedCurrency = request.Currency.Trim().ToUpperInvariant();
        var drafts = new List<OrderItemDraft>(request.Items.Count);

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return AppResult<CreateOrderOutcome>.Failure(
                    ErrorType.Unprocessable,
                    "product_not_found",
                    $"Product '{item.ProductId}' does not exist.",
                    "items.productId");
            }

            if (!string.Equals(product.Currency, normalizedCurrency, StringComparison.Ordinal))
            {
                return AppResult<CreateOrderOutcome>.Failure(
                    ErrorType.Unprocessable,
                    "currency_mismatch",
                    $"Product '{item.ProductId}' is priced in {product.Currency}.",
                    "currency");
            }

            if (product.AvailableQuantity < item.Quantity)
            {
                return AppResult<CreateOrderOutcome>.Failure(
                    ErrorType.Conflict,
                    "insufficient_stock",
                    $"Product '{item.ProductId}' does not have enough stock.",
                    "items.quantity");
            }

            drafts.Add(new OrderItemDraft(item.ProductId, product.UnitPrice, item.Quantity));
        }

        var placed = Order.Place(request.CustomerId, request.Currency, drafts, idempotencyKey, hash, clock.UtcNow);
        if (!placed.IsSuccess)
        {
            var error = placed.Error!;
            return AppResult<CreateOrderOutcome>.Failure(ErrorType.Validation, error.Code, error.Message, error.Field);
        }

        return await store.CreateAsync(placed.Value!, cancellationToken);
    }
}

