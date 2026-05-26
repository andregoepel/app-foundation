using System.Security.Claims;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Components.Authorization;

namespace AndreGoepel.Marten.Identity.Services;

public class CurrentUserService(AuthenticationStateProvider authStateProvider) : ICurrentUserService
{
    public async Task<UserId> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var idClaim = authState.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idClaim, out var guid) ? UserId.Parse(guid) : default;
    }
}
