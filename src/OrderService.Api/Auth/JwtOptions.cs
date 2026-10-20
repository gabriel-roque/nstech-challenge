namespace OrderService.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public int LifetimeMinutes { get; init; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JWT issuer and audience are required.");
        }

        if (SigningKey.Length < 32)
        {
            throw new InvalidOperationException("JWT signing key must contain at least 32 characters.");
        }

        if (LifetimeMinutes is < 1 or > 1_440)
        {
            throw new InvalidOperationException("JWT lifetime must be between 1 and 1440 minutes.");
        }
    }
}

