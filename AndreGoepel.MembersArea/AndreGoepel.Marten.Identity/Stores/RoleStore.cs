using AndreGoepel.Marten.Identity.Events;
using Marten;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.Marten.Identity.Stores;

public class RoleStore<TRole>(IDocumentSession session, ILogger<RoleStore<TRole>> logger)
    : IRoleStore<TRole>,
        IQueryableRoleStore<TRole>
    where TRole : IdentityRole
{
    public IQueryable<TRole> Roles => session.Query<TRole>();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public async Task<IdentityResult> CreateAsync(TRole role, CancellationToken cancellationToken)
    {
        try
        {
            if (role.Name == null)
                return IdentityResult.Failed(
                    new IdentityError() { Description = "Role name cannot be null." }
                );

            var roleId = RoleId.New();
            session.Events.Append(
                roleId.Value,
                new RoleCreated { RoleId = roleId, RoleName = role.Name }
            );
            await session.SaveChangesAsync(cancellationToken);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create the role in Marten.");
            return IdentityResult.Failed(
                new IdentityError() { Description = "Something went wrong saving the role." }
            );
        }
    }

    public async Task<IdentityResult> UpdateAsync(TRole role, CancellationToken cancellationToken)
    {
        try
        {
            session.Update(role);
            await session.SaveChangesAsync(cancellationToken);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update the role in Marten.");
            return IdentityResult.Failed(
                new IdentityError() { Description = "Something went wrong saving the role." }
            );
        }
    }

    public async Task<IdentityResult> DeleteAsync(TRole role, CancellationToken cancellationToken)
    {
        try
        {
            session.Delete(role);
            await session.SaveChangesAsync(cancellationToken);

            return IdentityResult.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete the role in Marten.");
            return IdentityResult.Failed(
                new IdentityError() { Description = "Something went wrong deleting the role." }
            );
        }
    }

    public Task<string> GetRoleIdAsync(TRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(TRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Name);

    public Task SetRoleNameAsync(TRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(
        TRole role,
        CancellationToken cancellationToken
    ) => Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(
        TRole role,
        string? normalizedName,
        CancellationToken cancellationToken
    )
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<TRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        await session.Query<TRole>().FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);

    public async Task<TRole?> FindByNameAsync(
        string? normalizedRoleName,
        CancellationToken cancellationToken
    ) =>
        await session
            .Query<TRole>()
            .FirstOrDefaultAsync(x => x.NormalizedName == normalizedRoleName, cancellationToken);
}
