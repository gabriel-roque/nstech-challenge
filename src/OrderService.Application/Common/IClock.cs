namespace OrderService.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

