using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.UserRoles;

public class UserRoleAssignment
{
    public string Id => $"{UserId}:{RoleId}";
    public Guid UserGuid => UserId;
    public UserId UserId { get; set; }
    public Guid RoleGuid => RoleId;
    public RoleId RoleId { get; set; }
}
