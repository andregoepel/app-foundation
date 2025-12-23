using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users;

public class User : IdentityUser
{
    public Guid UserId { get; internal set; }
    public bool Deleted { get; internal set; }

    public UserId CreatedBy { get; internal set; }
    public DateTime CreatedAt { get; internal set; }
    public UserId ChangedBy { get; internal set; }
    public DateTime ChangedAt { get; internal set; }
    public UserId? DeletedBy { get; internal set; }
    public DateTime? DeletedAt { get; internal set; }
}
