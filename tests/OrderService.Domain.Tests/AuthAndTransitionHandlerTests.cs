using OrderService.Application.Abstractions;
using OrderService.Application.Auth;
using OrderService.Application.Common;
using OrderService.Application.Contracts;
using OrderService.Application.Orders;
using Xunit;

namespace OrderService.Domain.Tests;

public sealed class AuthAndTransitionHandlerTests
{
    [Fact]
    public async Task AuthenticationValidatesAndIssuesToken()
    {
        var user = new AuthenticatedUser(Guid.NewGuid(), "user01");
        var valid = new AuthenticateHandler(new FakeAuthenticator(user), new FakeTokenIssuer());
        var invalid = new AuthenticateHandler(new FakeAuthenticator(null), new FakeTokenIssuer());

        var token = await valid.HandleAsync(new AuthRequest("user01", "password"), CancellationToken.None);
        var credentials = await invalid.HandleAsync(new AuthRequest("user01", "wrong"), CancellationToken.None);
        var missing = await valid.HandleAsync(new AuthRequest("", ""), CancellationToken.None);

        Assert.True(token.IsSuccess);
        Assert.Equal("token", token.Value?.AccessToken);
        Assert.Equal("invalid_credentials", credentials.Error?.Code);
        Assert.Equal("credentials_required", missing.Error?.Code);
    }

    [Fact]
    public async Task TransitionHandlersDelegateToStore()
    {
        var store = new TransitionStore();
        var confirm = new ConfirmOrderHandler(store);
        var cancel = new CancelOrderHandler(store);
        var id = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await confirm.HandleAsync(id, customerId, CancellationToken.None);
        await cancel.HandleAsync(id, customerId, CancellationToken.None);

        Assert.True(store.ConfirmCalled);
        Assert.True(store.CancelCalled);
    }

    private sealed class FakeAuthenticator(AuthenticatedUser? user) : IUserAuthenticator
    {
        public Task<AuthenticatedUser?> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(user);
    }

    private sealed class FakeTokenIssuer : ITokenIssuer
    {
        public TokenResponse Issue(AuthenticatedUser user) => new("token", "Bearer", 3600);
    }

    private sealed class TransitionStore : IOrderStore
    {
        public bool ConfirmCalled { get; private set; }

        public bool CancelCalled { get; private set; }

        public Task<AppResult<OrderDetailsDto>> ConfirmAsync(
            Guid orderId,
            Guid customerId,
            CancellationToken cancellationToken)
        {
            ConfirmCalled = true;
            return Task.FromResult(AppResult<OrderDetailsDto>.Failure(ErrorType.NotFound, "not_found", "Not found"));
        }

        public Task<AppResult<OrderDetailsDto>> CancelAsync(
            Guid orderId,
            Guid customerId,
            CancellationToken cancellationToken)
        {
            CancelCalled = true;
            return Task.FromResult(AppResult<OrderDetailsDto>.Failure(ErrorType.NotFound, "not_found", "Not found"));
        }

        public Task<IdempotencyRecord?> FindByIdempotencyKeyAsync(
            Guid customerId,
            Guid key,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, ProductSnapshot>> GetProductsAsync(
            IReadOnlyCollection<Guid> productIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppResult<CreateOrderOutcome>> CreateAsync(
            OrderService.Domain.Orders.Order order,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
