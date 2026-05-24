using System.Security.Claims;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Microsoft.AspNetCore.Components.Authorization;

namespace AndreGoepel.Marten.Identity.Services;

public interface ICurrentUserService
{
    Task<UserId> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);
}

public class CurrentUserService(
    AuthenticationStateProvider authStateProvider,
    IQuerySession querySession
) : ICurrentUserService
{
    public async Task<UserId> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var currentUserName = authState.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return await QueryUserIdByNameAsync(currentUserName, cancellationToken);
    }

    protected virtual Task<UserId> QueryUserIdByNameAsync(
        string? userName,
        CancellationToken cancellationToken = default
    ) =>
        querySession
            .Query<User>()
            .Where(u => u.UserName == userName)
            .Select(u => u.UserId)
            .SingleOrDefaultAsync(cancellationToken);
}
