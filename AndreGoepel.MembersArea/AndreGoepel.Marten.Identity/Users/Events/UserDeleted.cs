using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserDeleted(UserId UserId)
{
    public UserId DeletedBy { get; init; } = UserId;
    public DateTime DeletedAt { get; init; } = DateTime.UtcNow;
}
