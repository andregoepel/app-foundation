using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.Marten.Identity.Users.Events;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserProjectionTests
{
    private readonly UserProjection _projection = new();

    #region UserCreated

    [Fact]
    public void Apply_UserCreated_SetsUserProperties()
    {
        // Arrange
        var userId = UserId.New();
        var @event = new UserCreated(userId, "alice@example.com", "alice@example.com", "hash123");
        var user = new User();

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal(userId, user.UserId);
        Assert.Equal("alice@example.com", user.UserName);
        Assert.Equal("ALICE@EXAMPLE.COM", user.NormalizedUserName);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("ALICE@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("hash123", user.PasswordHash);
    }

    [Fact]
    public void Apply_UserCreated_SetsAuditFields()
    {
        // Arrange
        var userId = UserId.New();
        var before = DateTimeOffset.UtcNow;
        var @event = new UserCreated(userId, "alice@example.com", "alice@example.com", null);
        var user = new User();

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal(userId, user.CreatedBy);
        Assert.Equal(userId, user.ChangedBy);
        Assert.True(user.CreatedAt >= before);
        Assert.Equal(user.CreatedAt, user.ChangedAt);
    }

    [Fact]
    public void Apply_UserCreated_DefaultDeletableTrue()
    {
        // Arrange
        var userId = UserId.New();
        var @event = new UserCreated(userId, null, null, null);
        var user = new User();

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.True(user.Deletable);
        Assert.False(user.RootUser);
    }

    [Fact]
    public void Apply_UserCreated_RootUserFlag()
    {
        // Arrange
        var userId = UserId.New();
        var @event = new UserCreated(userId, null, null, null)
        {
            RootUser = true,
            Deletable = false,
        };
        var user = new User();

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.True(user.RootUser);
        Assert.False(user.Deletable);
    }

    #endregion

    #region UserDeleted

    [Fact]
    public void Apply_UserDeleted_ClearsSensitiveData()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User
        {
            UserName = "alice",
            Email = "alice@example.com",
            PasswordHash = "hash",
        };
        var @event = new UserDeleted(userId);

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Null(user.UserName);
        Assert.Null(user.Email);
        Assert.Null(user.PasswordHash);
        Assert.True(user.Deleted);
    }

    [Fact]
    public void Apply_UserDeleted_SetsDeletedBy()
    {
        // Arrange
        var userId = UserId.New();
        var deletedBy = UserId.New();
        var deletedAt = DateTimeOffset.UtcNow;
        var @event = new UserDeleted(userId) { DeletedBy = deletedBy, DeletedAt = deletedAt };
        var user = new User();

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal(deletedBy, user.DeletedBy);
        Assert.Equal(deletedAt, user.DeletedAt);
    }

    #endregion

    #region UserUpdated

    [Fact]
    public void Apply_UserUpdated_UpdatesEmailAndNormalized()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User { Email = "old@example.com" };
        var @event = new UserUpdated(userId) { Email = "new@example.com", EmailConfirmed = true };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("NEW@EXAMPLE.COM", user.NormalizedEmail);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public void Apply_UserUpdated_NullEmail_DoesNotOverwrite()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User { Email = "original@example.com" };
        var @event = new UserUpdated(userId) { Email = null };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal("original@example.com", user.Email);
    }

    [Fact]
    public void Apply_UserUpdated_UpdatesUserNameAndNormalized()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User { UserName = "oldname" };
        var @event = new UserUpdated(userId) { UserName = "newname" };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal("newname", user.UserName);
        Assert.Equal("NEWNAME", user.NormalizedUserName);
    }

    [Fact]
    public void Apply_UserUpdated_NullUserName_DoesNotOverwrite()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User { UserName = "original" };
        var @event = new UserUpdated(userId) { UserName = null };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal("original", user.UserName);
    }

    [Fact]
    public void Apply_UserUpdated_UpdatesPasswordHash()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User { PasswordHash = "old" };
        var @event = new UserUpdated(userId) { PasswordHash = "newHash" };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal("newHash", user.PasswordHash);
    }

    [Fact]
    public void Apply_UserUpdated_NullPasswordHash_DoesNotOverwrite()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User { PasswordHash = "original" };
        var @event = new UserUpdated(userId) { PasswordHash = null };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal("original", user.PasswordHash);
    }

    [Fact]
    public void Apply_UserUpdated_UpdatesLockoutFields()
    {
        // Arrange
        var userId = UserId.New();
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        var user = new User();
        var @event = new UserUpdated(userId)
        {
            LockoutEnabled = true,
            LockoutEnd = lockoutEnd,
            AccessFailedCount = 3,
        };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.True(user.LockoutEnabled);
        Assert.Equal(lockoutEnd, user.LockoutEnd);
        Assert.Equal(3, user.AccessFailedCount);
    }

    [Fact]
    public void Apply_UserUpdated_UpdatesTwoFactor()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User();
        var @event = new UserUpdated(userId)
        {
            TwoFactorEnabled = true,
            AuthenticatorKey = "key123",
            RecoveryCodes = "code1;code2",
        };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.True(user.TwoFactorEnabled);
        Assert.Equal("key123", user.AuthenticatorKey);
        Assert.Equal("code1;code2", user.RecoveryCodes);
    }

    [Fact]
    public void Apply_UserUpdated_SetsChangedAuditFields()
    {
        // Arrange
        var userId = UserId.New();
        var updatedBy = UserId.New();
        var updatedAt = DateTimeOffset.UtcNow;
        var user = new User();
        var @event = new UserUpdated(userId) { UpdatedBy = updatedBy, UpdatedAt = updatedAt };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal(updatedBy, user.ChangedBy);
        Assert.Equal(updatedAt, user.ChangedAt);
    }

    #endregion

    #region Passkey events

    [Fact]
    public void Apply_PasskeyCreated_AddsPasskeyToDict()
    {
        // Arrange
        var userId = UserId.New();
        var credentialId = new byte[] { 1, 2, 3, 4 };
        var passkey = BuildPasskeyInfo(credentialId);
        var @event = new PasskeyCreated(userId, passkey);
        var user = new User();

        // Act
        _projection.Apply(@event, user);

        // Assert
        var key = Convert.ToBase64String(credentialId);
        Assert.True(user.Passkeys.ContainsKey(key));
        Assert.Equal(passkey, user.Passkeys[key].PasskeyInfo);
    }

    [Fact]
    public void Apply_PasskeyUpdated_ReplacesExistingPasskey()
    {
        // Arrange
        var userId = UserId.New();
        var credentialId = new byte[] { 1, 2, 3, 4 };
        var original = BuildPasskeyInfo(credentialId);
        var updated = BuildPasskeyInfo(credentialId);
        var user = new User();
        var key = Convert.ToBase64String(credentialId);
        user.Passkeys[key] = new UserPasskey { PasskeyInfo = original };

        // Act
        _projection.Apply(new PasskeyUpdated(userId, updated), user);

        // Assert
        Assert.Equal(updated, user.Passkeys[key].PasskeyInfo);
    }

    [Fact]
    public void Apply_PasskeyDeleted_RemovesPasskey()
    {
        // Arrange
        var userId = UserId.New();
        var credentialId = new byte[] { 1, 2, 3, 4 };
        var key = Convert.ToBase64String(credentialId);
        var user = new User();
        user.Passkeys[key] = new UserPasskey { PasskeyInfo = BuildPasskeyInfo(credentialId) };

        // Act
        _projection.Apply(new PasskeyDeleted(userId, credentialId), user);

        // Assert
        Assert.False(user.Passkeys.ContainsKey(key));
    }

    [Fact]
    public void Apply_PasskeyDeleted_UnknownCredential_DoesNotThrow()
    {
        // Arrange
        var userId = UserId.New();
        var user = new User();

        // Act / Assert
        var exception = Record.Exception(() =>
            _projection.Apply(new PasskeyDeleted(userId, new byte[] { 9, 9, 9 }), user)
        );

        Assert.Null(exception);
    }

    #endregion

    #region Role events

    [Fact]
    public void Apply_RoleAssigned_AddsRoleToSet()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();
        var assignedBy = UserId.New();
        var user = new User();
        var @event = new RoleAssigned(userId, roleId, assignedBy);

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Contains(roleId, user.Roles);
    }

    [Fact]
    public void Apply_RoleAssigned_SetsChangedAuditFields()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();
        var assignedBy = UserId.New();
        var assignedAt = DateTimeOffset.UtcNow;
        var user = new User();
        var @event = new RoleAssigned(userId, roleId, assignedBy) { AssignedAt = assignedAt };

        // Act
        _projection.Apply(@event, user);

        // Assert
        Assert.Equal(assignedBy, user.ChangedBy);
        Assert.Equal(assignedAt, user.ChangedAt);
    }

    [Fact]
    public void Apply_RoleAssigned_DuplicateRole_StillSingleEntry()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();
        var user = new User();
        var @event = new RoleAssigned(userId, roleId, userId);

        // Act
        _projection.Apply(@event, user);
        _projection.Apply(@event, user);

        // Assert
        Assert.Single(user.Roles);
    }

    [Fact]
    public void Apply_RoleUnassigned_RemovesRole()
    {
        // Arrange
        var userId = UserId.New();
        var roleId = RoleId.New();
        var user = new User();
        user.Roles.Add(roleId);

        // Act
        _projection.Apply(new RoleUnassigned(userId, roleId, userId), user);

        // Assert
        Assert.DoesNotContain(roleId, user.Roles);
    }

    [Fact]
    public void Apply_RoleUnassigned_OtherRolesUnaffected()
    {
        // Arrange
        var userId = UserId.New();
        var roleToRemove = RoleId.New();
        var roleToKeep = RoleId.New();
        var user = new User();
        user.Roles.Add(roleToRemove);
        user.Roles.Add(roleToKeep);

        // Act
        _projection.Apply(new RoleUnassigned(userId, roleToRemove, userId), user);

        // Assert
        Assert.Contains(roleToKeep, user.Roles);
        Assert.DoesNotContain(roleToRemove, user.Roles);
    }

    #endregion

    #region Helpers

    private static UserPasskeyInfo BuildPasskeyInfo(byte[] credentialId) =>
        new(
            credentialId,
            publicKey: [1],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: null,
            isUserVerified: false,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: [],
            clientDataJson: []
        );

    #endregion
}
