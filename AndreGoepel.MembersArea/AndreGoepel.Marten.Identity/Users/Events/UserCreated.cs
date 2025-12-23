namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserCreated(UserId UserId, string? UserName, string? Email, string? PasswordHash)
{
    public UserId CreatedBy { get; } = UserId;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}
