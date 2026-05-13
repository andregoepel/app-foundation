using AndreGoepel.Marten.Identity.Roles;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users;

public class User : IdentityUser
{
    public override string Id
    {
        get => UserId.ToString();
        set => UserId = UserId.Parse(value);
    }
    public UserId UserId { get; set; }
    public Guid StreamId
    {
        get => UserId.Value;
        set => UserId = UserId.Parse(value);
    }

    public bool RootUser { get; set; }
    public bool Deletable { get; set; } = true;
    public bool Deleted { get; set; }
    public string? AuthenticatorKey { get; set; }
    public string? RecoveryCodes { get; set; }

    // Todo: Use Hashset
    public Dictionary<string, UserPasskey> Passkeys { get; set; } = [];

    public HashSet<RoleId> Roles { get; set; } = [];

    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public UserId ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public UserId? DeletedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
