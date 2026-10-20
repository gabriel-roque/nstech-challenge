using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Contracts;

namespace OrderService.Api.Auth;

public sealed class JwtTokenIssuer(JwtOptions options, IClock clock) : ITokenIssuer
{
    public TokenResponse Issue(AuthenticatedUser user)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(options.LifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7(now).ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            credentials);

        return new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            checked(options.LifetimeMinutes * 60));
    }
}
