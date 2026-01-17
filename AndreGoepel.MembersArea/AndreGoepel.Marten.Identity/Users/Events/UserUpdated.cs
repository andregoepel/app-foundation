namespace AndreGoepel.Marten.Identity.Users.Events;

public record UserUpdated(UserId UserId)
{
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? PasswordHash { get; init; }
    public string? PhoneNumber { get; init; }
    public string? AuthenticatorKey { get; init; }
    public bool EmailConfirmed { get; init; }
    public UserId UpdatedBy { get; init; } = UserId;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool TwoFactorEnabled { get; internal set; }
    public string? RecoveryCodes { get; internal set; }
}
