namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserRestored(UserId UserId, UserId RestoredBy)
{
    public DateTimeOffset RestoredAt { get; init; } = DateTimeOffset.UtcNow;

    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? PasswordHash { get; init; }
}
