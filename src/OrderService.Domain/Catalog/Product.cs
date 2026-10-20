namespace OrderService.Domain.Catalog;

public sealed class Product
{
    private Product()
    {
        Sku = string.Empty;
        Name = string.Empty;
        Currency = string.Empty;
    }

    public Product(
        Guid id,
        string sku,
        string name,
        string currency,
        decimal unitPrice,
        int availableQuantity,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);
        ArgumentOutOfRangeException.ThrowIfNegative(availableQuantity);

        Id = id;
        Sku = sku;
        Name = name;
        Currency = currency.Trim().ToUpperInvariant();
        UnitPrice = unitPrice;
        AvailableQuantity = availableQuantity;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public string Currency { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int AvailableQuantity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

