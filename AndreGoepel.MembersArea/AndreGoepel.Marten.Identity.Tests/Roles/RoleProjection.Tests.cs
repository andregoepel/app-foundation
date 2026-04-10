using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Roles.Events;
using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Tests.Roles;

public class RoleProjectionTests
{
    private readonly RoleProjection _projection = new();

    // ── RoleCreated ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_RoleCreated_SetsProperties()
    {
        var roleId = RoleId.New();
        var createdBy = UserId.New();
        var @event = new RoleCreated(roleId, "Admin", createdBy);
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.Equal(roleId, role.RoleId);
        Assert.Equal("Admin", role.Name);
        Assert.Equal("ADMIN", role.NormalizedName);
        Assert.True(role.Deletable);
    }

    [Fact]
    public void Apply_RoleCreated_SetsAuditFields()
    {
        var roleId = RoleId.New();
        var createdBy = UserId.New();
        var createdAt = DateTimeOffset.UtcNow;
        var @event = new RoleCreated(roleId, "Admin", createdBy) { CreatedAt = createdAt };
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.Equal(createdBy, role.CreatedBy);
        Assert.Equal(createdAt, role.CreatedAt);
        Assert.Equal(createdBy, role.ChangedBy);
        Assert.Equal(createdAt, role.ChangedAt);
    }

    [Fact]
    public void Apply_RoleCreated_DeletableFalse()
    {
        var roleId = RoleId.New();
        var @event = new RoleCreated(roleId, "System", UserId.New()) { Deletable = false };
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.False(role.Deletable);
    }

    [Fact]
    public void Apply_RoleCreated_NormalizesNameToUpperInvariant()
    {
        var @event = new RoleCreated(RoleId.New(), "SuperAdmin", UserId.New());
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.Equal("SUPERADMIN", role.NormalizedName);
    }

    // ── RoleChanged ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_RoleChanged_UpdatesNameAndNormalized()
    {
        var roleId = RoleId.New();
        var @event = new RoleChanged(roleId, "Moderator", UserId.New());
        var role = new Role { Name = "OldName" };

        _projection.Apply(@event, role);

        Assert.Equal("Moderator", role.Name);
        Assert.Equal("MODERATOR", role.NormalizedName);
    }

    [Fact]
    public void Apply_RoleChanged_UpdatesDeletable()
    {
        var roleId = RoleId.New();
        var @event = new RoleChanged(roleId, "Admin", UserId.New()) { Deletable = false };
        var role = new Role { Deletable = true };

        _projection.Apply(@event, role);

        Assert.False(role.Deletable);
    }

    [Fact]
    public void Apply_RoleChanged_SetsChangedAuditFields()
    {
        var changedBy = UserId.New();
        var changedAt = DateTimeOffset.UtcNow;
        var @event = new RoleChanged(RoleId.New(), "Admin", changedBy) { ChangedAt = changedAt };
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.Equal(changedBy, role.ChangedBy);
        Assert.Equal(changedAt, role.ChangedAt);
    }

    // ── RoleDeleted ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_RoleDeleted_SetsDeletedFlag()
    {
        var @event = new RoleDeleted(RoleId.New(), UserId.New());
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.True(role.Deleted);
    }

    [Fact]
    public void Apply_RoleDeleted_SetsDeletedByAndAt()
    {
        var deletedBy = UserId.New();
        var deletedAt = DateTimeOffset.UtcNow;
        var @event = new RoleDeleted(RoleId.New(), deletedBy) { DeletedAt = deletedAt };
        var role = new Role();

        _projection.Apply(@event, role);

        Assert.Equal(deletedBy, role.DeletedBy);
        Assert.Equal(deletedAt, role.DeletedAt);
    }

    [Fact]
    public void Apply_RoleDeleted_DoesNotClearName()
    {
        var @event = new RoleDeleted(RoleId.New(), UserId.New());
        var role = new Role { Name = "Admin" };

        _projection.Apply(@event, role);

        // Role name is preserved on delete (unlike user email/password)
        Assert.Equal("Admin", role.Name);
    }
}
