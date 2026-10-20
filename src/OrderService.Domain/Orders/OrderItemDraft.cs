namespace OrderService.Domain.Orders;

public sealed record OrderItemDraft(Guid ProductId, decimal UnitPrice, int Quantity);

