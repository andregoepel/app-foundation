using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Roles.Events;

public record RoleRestored(RoleId RoleId, UserId RestoredBy)
{
    public DateTimeOffset RestoredAt { get; init; } = DateTimeOffset.UtcNow;
}
