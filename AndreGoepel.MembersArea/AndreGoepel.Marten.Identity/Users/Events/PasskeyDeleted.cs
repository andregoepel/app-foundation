using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record PasskeyDeleted(UserId UserId, byte[] CredentialId)
{
    public UserId DeletedBy { get; init; } = UserId;
    public DateTime DeletedAt { get; init; } = DateTime.UtcNow;
}
