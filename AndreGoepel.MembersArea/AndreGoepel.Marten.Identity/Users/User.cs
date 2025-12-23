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

    public UserId CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserId ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public UserId? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
