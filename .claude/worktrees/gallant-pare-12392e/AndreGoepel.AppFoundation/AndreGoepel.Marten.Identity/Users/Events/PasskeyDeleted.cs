namespace AndreGoepel.Marten.Identity.Users.Events;

public record PasskeyDeleted(UserId UserId, byte[] CredentialId)
{
    public UserId DeletedBy { get; init; } = UserId;
    public DateTimeOffset DeletedAt { get; init; } = DateTimeOffset.UtcNow;
}
