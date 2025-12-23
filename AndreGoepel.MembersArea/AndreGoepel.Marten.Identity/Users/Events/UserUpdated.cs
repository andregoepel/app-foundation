using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserUpdated(UserId userId)
{
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? PasswordHash { get; init; }
    public bool EmailConfirmed { get; init; }
    public UserId UpdatedBy { get; } = userId;
    public DateTime UpdatedAt { get; } = DateTime.UtcNow;
}
