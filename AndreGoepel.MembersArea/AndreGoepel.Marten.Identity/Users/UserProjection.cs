using System.Diagnostics.CodeAnalysis;
using AndreGoepel.Marten.Identity.Users.Events;
using Marten.Events.Aggregation;

namespace AndreGoepel.Marten.Identity.Users;

internal class UserProjection : SingleStreamProjection<User, Guid>
{
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(UserCreated @event, User user)
    {
        user.UserId = @event.UserId;
        user.UserName = @event.UserName;
        user.NormalizedUserName = @event.UserName?.ToUpperInvariant();
        user.Email = @event.Email;
        user.NormalizedEmail = @event.Email?.ToUpperInvariant();
        user.PasswordHash = @event.PasswordHash;
        user.Deletable = @event.Deletable;
        user.RootUser = @event.RootUser;
        user.CreatedBy = @event.CreatedBy;
        user.CreatedAt = @event.CreatedAt;
        user.ChangedBy = @event.CreatedBy;
        user.ChangedAt = @event.CreatedAt;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(UserDeleted @event, User user)
    {
        user.UserName = null;
        user.Email = null;
        user.PasswordHash = null;
        user.Deleted = true;
        user.DeletedBy = @event.DeletedBy;
        user.DeletedAt = @event.DeletedAt;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(UserUpdated @event, User user)
    {
        if (@event.UserName is not null)
        {
            user.UserName = @event.UserName;
            user.NormalizedUserName = @event.UserName?.ToUpperInvariant();
        }

        user.EmailConfirmed = @event.EmailConfirmed;
        if (@event.Email is not null)
        {
            user.Email = @event.Email;
            user.NormalizedEmail = @event.Email?.ToUpperInvariant();
        }

        if (@event.PhoneNumber is not null)
            user.PhoneNumber = @event.PhoneNumber;

        if (@event.PasswordHash is not null)
            user.PasswordHash = @event.PasswordHash;

        #region TwoFactor Authentication

        if (@event.AuthenticatorKey is not null)
            user.AuthenticatorKey = @event.AuthenticatorKey;

        if (@event.RecoveryCodes is not null)
            user.RecoveryCodes = @event.RecoveryCodes;

        user.TwoFactorEnabled = @event.TwoFactorEnabled;

        #endregion TwoFactor Authentication

        user.Deletable = @event.Deletable;

        user.ChangedBy = @event.UpdatedBy;
        user.ChangedAt = @event.UpdatedAt;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(PasskeyCreated @event, User user)
    {
        var passkeyInfo = new UserPasskey { PasskeyInfo = @event.Passkey };
        user.Passkeys[passkeyInfo.CredentialId] = passkeyInfo;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(PasskeyUpdated @event, User user)
    {
        var passkeyInfo = new UserPasskey { PasskeyInfo = @event.Passkey };
        user.Passkeys[passkeyInfo.CredentialId] = passkeyInfo;
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(PasskeyDeleted @event, User user)
    {
        user.Passkeys.Remove(Convert.ToBase64String(@event.CredentialId));
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(RoleAssigned @event, User user)
    {
        user.Roles.Add(@event.RoleId);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Called by Marten via reflection"
    )]
    public void Apply(RoleUnassigned @event, User user)
    {
        user.Roles.Remove(@event.RoleId);
    }
}
