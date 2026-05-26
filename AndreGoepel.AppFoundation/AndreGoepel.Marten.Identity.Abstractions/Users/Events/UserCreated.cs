namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserCreated(UserId UserId, string? UserName, string? Email, string? PasswordHash)
{
    public bool RootUser { get; init; }
    public bool Deletable { get; init; } = true;

    public UserId CreatedBy { get; init; } = UserId;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
