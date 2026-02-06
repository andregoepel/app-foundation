using System.Security.Claims;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Microsoft.AspNetCore.Components.Authorization;

namespace AndreGoepel.Marten.Identity.Services;

public interface ICurrentUserService
{
    Task<UserId> GetCurrentUserIdAsync();
}

public class CurrentUserService(
    AuthenticationStateProvider authStateProvider,
    IQuerySession querySession
) : ICurrentUserService
{
    public async Task<UserId> GetCurrentUserIdAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();

        var currentUserName = authState.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return await querySession
            .Query<User>()
            .Where(u => u.UserName == currentUserName)
            .Select(u => u.UserId)
            .SingleOrDefaultAsync();
    }
}
