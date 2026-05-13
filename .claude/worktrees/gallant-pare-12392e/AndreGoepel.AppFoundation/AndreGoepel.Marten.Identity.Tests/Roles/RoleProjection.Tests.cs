using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Roles.Events;
using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Tests.Roles;

public class RoleProjectionTests
{
    private readonly RoleProjection _projection = new();

    #region RoleCreated

    [Fact]
    public void Apply_RoleCreated_SetsProperties()
    {
        // Arrange
        var roleId = RoleId.New();
        var createdBy = UserId.New();
        var @event = new RoleCreated(roleId, "Admin", createdBy);
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.Equal(roleId, role.RoleId);
        Assert.Equal("Admin", role.Name);
        Assert.Equal("ADMIN", role.NormalizedName);
        Assert.True(role.Deletable);
    }

    [Fact]
    public void Apply_RoleCreated_SetsAuditFields()
    {
        // Arrange
        var roleId = RoleId.New();
        var createdBy = UserId.New();
        var createdAt = DateTimeOffset.UtcNow;
        var @event = new RoleCreated(roleId, "Admin", createdBy) { CreatedAt = createdAt };
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.Equal(createdBy, role.CreatedBy);
        Assert.Equal(createdAt, role.CreatedAt);
        Assert.Equal(createdBy, role.ChangedBy);
        Assert.Equal(createdAt, role.ChangedAt);
    }

    [Fact]
    public void Apply_RoleCreated_DeletableFalse()
    {
        // Arrange
        var roleId = RoleId.New();
        var @event = new RoleCreated(roleId, "System", UserId.New()) { Deletable = false };
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.False(role.Deletable);
    }

    [Fact]
    public void Apply_RoleCreated_NormalizesNameToUpperInvariant()
    {
        // Arrange
        var @event = new RoleCreated(RoleId.New(), "SuperAdmin", UserId.New());
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.Equal("SUPERADMIN", role.NormalizedName);
    }

    #endregion

    #region RoleChanged

    [Fact]
    public void Apply_RoleChanged_UpdatesNameAndNormalized()
    {
        // Arrange
        var roleId = RoleId.New();
        var @event = new RoleChanged(roleId, "Moderator", UserId.New());
        var role = new Role { Name = "OldName" };

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.Equal("Moderator", role.Name);
        Assert.Equal("MODERATOR", role.NormalizedName);
    }

    [Fact]
    public void Apply_RoleChanged_UpdatesDeletable()
    {
        // Arrange
        var roleId = RoleId.New();
        var @event = new RoleChanged(roleId, "Admin", UserId.New()) { Deletable = false };
        var role = new Role { Deletable = true };

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.False(role.Deletable);
    }

    [Fact]
    public void Apply_RoleChanged_SetsChangedAuditFields()
    {
        // Arrange
        var changedBy = UserId.New();
        var changedAt = DateTimeOffset.UtcNow;
        var @event = new RoleChanged(RoleId.New(), "Admin", changedBy) { ChangedAt = changedAt };
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.Equal(changedBy, role.ChangedBy);
        Assert.Equal(changedAt, role.ChangedAt);
    }

    #endregion

    #region RoleDeleted

    [Fact]
    public void Apply_RoleDeleted_SetsDeletedFlag()
    {
        // Arrange
        var @event = new RoleDeleted(RoleId.New(), UserId.New());
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.True(role.Deleted);
    }

    [Fact]
    public void Apply_RoleDeleted_SetsDeletedByAndAt()
    {
        // Arrange
        var deletedBy = UserId.New();
        var deletedAt = DateTimeOffset.UtcNow;
        var @event = new RoleDeleted(RoleId.New(), deletedBy) { DeletedAt = deletedAt };
        var role = new Role();

        // Act
        _projection.Apply(@event, role);

        // Assert
        Assert.Equal(deletedBy, role.DeletedBy);
        Assert.Equal(deletedAt, role.DeletedAt);
    }

    [Fact]
    public void Apply_RoleDeleted_DoesNotClearName()
    {
        // Arrange
        var @event = new RoleDeleted(RoleId.New(), UserId.New());
        var role = new Role { Name = "Admin" };

        // Act
        _projection.Apply(@event, role);

        // Assert
        // Role name is preserved on delete (unlike user email/password)
        Assert.Equal("Admin", role.Name);
    }

    #endregion
}
