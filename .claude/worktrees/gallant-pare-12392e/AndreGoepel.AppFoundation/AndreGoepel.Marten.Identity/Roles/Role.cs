using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Roles;

public class Role : IdentityRole
{
    public override string Id
    {
        get => RoleId.ToString();
        set => RoleId = RoleId.Parse(value);
    }

    public RoleId RoleId { get; set; }

    public Guid StreamId
    {
        get => RoleId.Value;
        set => RoleId = RoleId.Parse(value);
    }

    public bool Deletable { get; set; } = true;
    public bool Deleted { get; set; }

    public UserId CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public UserId ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public UserId? DeletedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
