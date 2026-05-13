using AndreGoepel.Marten.Identity.Roles;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record RoleAssigned(UserId UserId, RoleId RoleId, UserId AssignedBy)
{
    public DateTimeOffset AssignedAt { get; init; } = DateTimeOffset.UtcNow;
}
