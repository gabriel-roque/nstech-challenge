using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OrderService.Application.Contracts;

namespace OrderService.Application.Orders;

public static class RequestFingerprint
{
    public static string Compute(CreateOrderRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(request.CustomerId.ToString("N", CultureInfo.InvariantCulture));
        builder.Append('|');
        builder.Append(request.Currency.Trim().ToUpperInvariant());

        foreach (var item in request.Items.OrderBy(item => item.ProductId))
        {
            builder.Append('|');
            builder.Append(item.ProductId.ToString("N", CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(item.Quantity.ToString(CultureInfo.InvariantCulture));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}

