namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserLogin(UserId userId)
{
    public DateTimeOffset LoginAt { get; init; } = DateTimeOffset.Now;
}
