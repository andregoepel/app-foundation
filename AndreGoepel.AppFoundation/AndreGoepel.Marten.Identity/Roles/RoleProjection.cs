using System.Diagnostics.CodeAnalysis;
using AndreGoepel.Marten.Identity.Roles.Events;
using Marten.Events.Aggregation;

namespace AndreGoepel.Marten.Identity.Roles;

internal partial class RoleProjection : SingleStreamProjection<Role, Guid>
{
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten source-generated dispatcher"
    )]
    public void Apply(RoleCreated @event, Role role)
    {
        role.RoleId = @event.RoleId;
        role.Name = @event.Name;
        role.NormalizedName = @event.Name.ToUpperInvariant();
        role.Deletable = @event.Deletable;
        role.CreatedBy = @event.CreatedBy;
        role.CreatedAt = @event.CreatedAt;
        role.ChangedBy = @event.CreatedBy;
        role.ChangedAt = @event.CreatedAt;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten source-generated dispatcher"
    )]
    public void Apply(RoleChanged @event, Role role)
    {
        role.Name = @event.Name;
        role.NormalizedName = @event.Name.ToUpperInvariant();
        role.Deletable = @event.Deletable;
        role.ChangedBy = @event.ChangedBy;
        role.ChangedAt = @event.ChangedAt;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten source-generated dispatcher"
    )]
    public void Apply(RoleDeleted @event, Role role)
    {
        role.Deleted = true;
        role.DeletedBy = @event.DeletedBy;
        role.DeletedAt = @event.DeletedAt;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten source-generated dispatcher"
    )]
    public void Apply(RoleRestored @event, Role role)
    {
        role.Deleted = false;
        role.DeletedBy = null;
        role.DeletedAt = null;
    }
}
