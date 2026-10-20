namespace OrderService.Domain.Orders;

public sealed class OrderItem
{
    private OrderItem()
    {
    }

    internal OrderItem(Guid orderId, Guid productId, decimal unitPrice, int quantity)
    {
        OrderId = orderId;
        ProductId = productId;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal Subtotal => UnitPrice * Quantity;
}

