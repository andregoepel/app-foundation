using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record PasskeyCreated(UserId UserId, UserPasskeyInfo Passkey)
{
    public UserId CreatedBy { get; init; } = UserId;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
