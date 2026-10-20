namespace OrderService.Application.Common;

public sealed record AppError(ErrorType Type, string Code, string Message, string? Field = null);

