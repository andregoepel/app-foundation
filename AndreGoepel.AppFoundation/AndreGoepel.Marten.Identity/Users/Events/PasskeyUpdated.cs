using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record PasskeyUpdated(UserId UserId, UserPasskeyInfo Passkey)
{
    public UserId UpdatedBy { get; init; } = UserId;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
