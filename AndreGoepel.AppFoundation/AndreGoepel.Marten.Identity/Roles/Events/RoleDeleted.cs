using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Roles.Events;

public record RoleDeleted(RoleId RoleId, UserId DeletedBy)
{
    public DateTimeOffset DeletedAt { get; init; } = DateTimeOffset.UtcNow;
}
