using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users;

public class User : IdentityUser
{
    public override string Id
    {
        get => UserId.ToString();
        set => UserId = Guid.Parse(value);
    }
    public Guid UserId { get; set; }
    public bool Deleted { get; set; }
    public string? AuthenticatorKey { get; set; }
    public string? RecoveryCodes { get; set; }

    // Todo: Use Hashset
    public Dictionary<string, UserPasskey> Passkeys { get; set; } = [];

    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public UserId ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public UserId? DeletedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
