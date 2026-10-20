using OrderService.Application.Contracts;

namespace OrderService.Application.Abstractions;

public interface IUserAuthenticator
{
    Task<AuthenticatedUser?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
}

