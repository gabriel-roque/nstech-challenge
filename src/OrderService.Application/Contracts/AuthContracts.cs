namespace OrderService.Application.Contracts;

public sealed record AuthRequest(string Username, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed record AuthenticatedUser(Guid Id, string Username);

