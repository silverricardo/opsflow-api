namespace OpsFlow.Domain.Users;

/// <summary>A person who can authenticate. Users are created by seeding or administration, never by self sign-up.</summary>
public sealed class User
{
    public const int EmailMaxLength = 256;
    public const int DisplayNameMaxLength = 100;

    private User() { } // EF Core

    public User(string email, string displayName, UserRole role, int? managerId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Email = NormalizeEmail(email);
        DisplayName = displayName.Trim();
        Role = role;
        ManagerId = managerId;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }

    /// <summary>Stored normalized (trimmed, lower-case) so the unique index is case-insensitive in practice.</summary>
    public string Email { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public int? ManagerId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void Deactivate() => IsActive = false;
}
