namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserCreated(UserId UserId, string? UserName, string? Email, string? PasswordHash)
{
    public UserId CreatedBy { get; init; } = UserId;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
