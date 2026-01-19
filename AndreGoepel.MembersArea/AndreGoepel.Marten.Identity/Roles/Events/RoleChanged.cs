using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Roles.Events;

public record RoleChanged(RoleId RoleId, string Name, UserId ChangedBy)
{
    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;
}
