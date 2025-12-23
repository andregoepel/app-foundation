using AndreGoepel.Marten.Identity.Users.Events;
using Marten.Events.Aggregation;

namespace AndreGoepel.Marten.Identity.Users;

internal class UserProjection : SingleStreamProjection<User, Guid>
{
    public void Apply(UserCreated @event, User user)
    {
        user.Id = @event.UserId.Value.ToString();
        user.UserId = @event.UserId.Value;
        user.UserName = @event.UserName;
        user.NormalizedUserName = @event.UserName?.ToUpper();
        user.Email = @event.Email;
        user.NormalizedEmail = @event.Email?.ToUpper();
        user.PasswordHash = @event.PasswordHash;
        user.CreatedBy = @event.CreatedBy;
        user.CreatedAt = @event.CreatedAt;
        user.ChangedBy = @event.CreatedBy;
        user.ChangedAt = @event.CreatedAt;
    }

    public void Apply(UserDeleted @event, User user)
    {
        user.UserName = null;
        user.Email = null;
        user.PasswordHash = null;
        user.Deleted = true;
        user.DeletedBy = @event.DeletedBy;
        user.DeletedAt = @event.DeletedAt;
    }

    public void Apply(UserUpdated @event, User user)
    {
        if (@event.UserName is not null)
        {
            user.UserName = @event.UserName;
            user.NormalizedUserName = @event.UserName?.ToUpper();
        }
        if (@event.Email is not null)
        {
            user.Email = @event.Email;
            user.NormalizedEmail = @event.Email?.ToUpper();
        }
        if (@event.PasswordHash is not null)
        {
            user.PasswordHash = @event.PasswordHash;
        }
        user.EmailConfirmed = @event.EmailConfirmed;
        user.ChangedBy = @event.UpdatedBy;
        user.ChangedAt = @event.UpdatedAt;
    }
}
