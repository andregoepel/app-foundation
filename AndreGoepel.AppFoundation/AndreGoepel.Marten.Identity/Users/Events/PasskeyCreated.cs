using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record PasskeyCreated(UserId UserId, UserPasskeyInfo Passkey)
{
    public UserId CreatedBy { get; init; } = UserId;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
