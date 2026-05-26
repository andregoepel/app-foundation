using AndreGoepel.Marten.Identity.Roles;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record RoleUnassigned(UserId UserId, RoleId RoleId, UserId UnassignedBy)
{
    public DateTimeOffset UnassignedAt { get; init; } = DateTimeOffset.UtcNow;
}
