using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace OrderService.Api.Auth;

internal static class ClaimsPrincipalExtensions
{
    public static bool TryGetCustomerId(this ClaimsPrincipal principal, out Guid customerId)
    {
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out customerId) && customerId != Guid.Empty;
    }
}
