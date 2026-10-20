namespace OrderService.Domain.Common;

public sealed record DomainError(string Code, string Message, string? Field = null);

