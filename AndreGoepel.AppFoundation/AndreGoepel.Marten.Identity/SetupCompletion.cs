using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using RoleNames = AndreGoepel.Marten.Identity.Roles.Roles;

namespace AndreGoepel.Marten.Identity;

/// <summary>
/// Single source of truth for "is initial setup complete?". The Setup page,
/// the redirect middleware, and the nav menu must all agree, or users get
/// stuck in inconsistent states (e.g. middleware says "configured" but the
/// app has no Administrator role to authorise the admin pages).
/// </summary>
public static class SetupCompletion
{
    public static async Task<bool> IsCompleteAsync(
        IQuerySession session,
        CancellationToken cancellationToken = default
    )
    {
        var administratorRoleName = RoleNames.Administrator.ToUpperInvariant();
        var hasAdministratorRole = await session
            .Query<Role>()
            .AnyAsync(
                r => r.NormalizedName == administratorRoleName && !r.Deleted,
                cancellationToken
            );
        if (!hasAdministratorRole)
            return false;

        return await session.Query<User>().AnyAsync(u => !u.Deleted, cancellationToken);
    }
}
