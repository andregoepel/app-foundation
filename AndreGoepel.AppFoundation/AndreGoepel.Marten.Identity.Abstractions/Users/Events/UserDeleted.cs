namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserDeleted(UserId UserId)
{
    public UserId DeletedBy { get; init; } = UserId;
    public DateTimeOffset DeletedAt { get; init; } = DateTimeOffset.UtcNow;
}
