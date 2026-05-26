using System.Security.Claims;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AndreGoepel.Marten.Identity.Blazor.Tests.TestSupport;

/// <summary>
/// Bundles a substituted <see cref="UserManager{TUser}"/> and a fake
/// <see cref="AuthenticationStateProvider"/> wired up around the same
/// in-memory <see cref="User"/>. Most Manage/* pages need exactly this.
/// </summary>
internal static class AuthenticatedUserContext
{
    public static UserManager<User> BuildUserManager(IdentityOptions? options = null)
    {
        var store = Substitute.For<IUserStore<User>>();
        return Substitute.For<UserManager<User>>(
            store,
            Options.Create(options ?? new IdentityOptions()),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!
        );
    }

    public static (AuthenticationStateProvider Provider, ClaimsPrincipal Principal) BuildAuthState(
        User user
    )
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
            authenticationType: "test"
        );
        var principal = new ClaimsPrincipal(identity);
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync().Returns(new AuthenticationState(principal));
        return (provider, principal);
    }
}
