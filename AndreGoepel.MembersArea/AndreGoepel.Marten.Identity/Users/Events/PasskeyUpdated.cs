using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record PasskeyUpdated(UserId UserId, UserPasskeyInfo Passkey)
{
    public UserId UpdatedBy { get; init; } = UserId;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
