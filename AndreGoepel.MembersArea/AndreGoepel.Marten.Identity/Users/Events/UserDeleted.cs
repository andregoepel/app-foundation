using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserDeleted(UserId UserId)
{
    public UserId DeletedBy { get; } = UserId;
    public DateTime DeletedAt { get; } = DateTime.UtcNow;
}
