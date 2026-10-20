using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Contracts;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Auth;

public sealed class UserAuthenticator(IDbContextFactory<OrderDbContext> contextFactory) : IUserAuthenticator
{
    public async Task<AuthenticatedUser?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = username.Trim().ToUpperInvariant();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.NormalizedUsername == normalized && candidate.IsActive, cancellationToken);

        return user is not null && PasswordHasher.Verify(password, user.PasswordHash)
            ? new AuthenticatedUser(user.Id, user.Username)
            : null;
    }
}
