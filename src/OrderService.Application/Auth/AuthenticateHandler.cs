using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Contracts;

namespace OrderService.Application.Auth;

public sealed class AuthenticateHandler(IUserAuthenticator authenticator, ITokenIssuer tokenIssuer)
{
    public async Task<AppResult<TokenResponse>> HandleAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AppResult<TokenResponse>.Failure(ErrorType.Validation, "credentials_required", "Username and password are required.");
        }

        var user = await authenticator.AuthenticateAsync(request.Username, request.Password, cancellationToken);
        return user is null
            ? AppResult<TokenResponse>.Failure(ErrorType.Unauthorized, "invalid_credentials", "Invalid credentials.")
            : AppResult<TokenResponse>.Success(tokenIssuer.Issue(user));
    }
}
