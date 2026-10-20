namespace OrderService.Domain.Users;

public sealed class User
{
    private User()
    {
        Username = string.Empty;
        NormalizedUsername = string.Empty;
        PasswordHash = string.Empty;
    }

    public User(Guid id, string username, string passwordHash, DateTimeOffset now)
    {
        Id = id;
        Username = username;
        NormalizedUsername = username.Trim().ToUpperInvariant();
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; }

    public string NormalizedUsername { get; private set; }

    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
