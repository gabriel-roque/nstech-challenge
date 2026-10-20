using OrderService.Application.Contracts;

namespace OrderService.Application.Abstractions;

public interface ITokenIssuer
{
    TokenResponse Issue(AuthenticatedUser user);
}

