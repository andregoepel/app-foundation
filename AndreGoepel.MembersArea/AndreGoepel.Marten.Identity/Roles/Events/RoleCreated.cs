using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Roles.Events;

public record RoleCreated(RoleId RoleId, string Name, UserId CreatedBy)
{
    public bool Deletable { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
