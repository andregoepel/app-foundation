using System.Security.Claims;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;

namespace AndreGoepel.Marten.Identity.Tests.Services;

public class CurrentUserServiceTests
{
    #region Helpers

    private static AuthenticationState AuthState(string? nameIdentifier)
    {
        if (nameIdentifier is null)
            return new AuthenticationState(new ClaimsPrincipal());

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, nameIdentifier) };
        var identity = new ClaimsIdentity(claims, "test");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private static AuthenticationState AuthState(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "test");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private static TestableCurrentUserService BuildService(
        AuthenticationState authState,
        List<User> users
    )
    {
        var authProvider = new FakeAuthStateProvider(authState);
        var session = Substitute.For<IQuerySession>();
        return new TestableCurrentUserService(authProvider, session, users);
    }

    #endregion

    #region Tests

    [Fact]
    public async Task GetCurrentUserIdAsync_MatchingUser_ReturnsUserId()
    {
        var userId = UserId.New();
        var user = new User { UserId = userId, UserName = "alice@example.com" };
        var service = BuildService(AuthState("alice@example.com"), [user]);

        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(userId, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_MultipleUsers_ReturnsCorrectUserId()
    {
        var aliceId = UserId.New();
        var bobId = UserId.New();
        var users = new List<User>
        {
            new() { UserId = aliceId, UserName = "alice@example.com" },
            new() { UserId = bobId, UserName = "bob@example.com" },
        };
        var service = BuildService(AuthState("bob@example.com"), users);

        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(bobId, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_NoMatchingUser_ReturnsDefault()
    {
        var service = BuildService(AuthState("unknown@example.com"), []);

        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_UnauthenticatedPrincipal_ReturnsDefault()
    {
        var user = new User { UserId = UserId.New(), UserName = "alice@example.com" };
        var service = BuildService(AuthState(nameIdentifier: null), [user]);

        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_WrongClaimType_ReturnsDefault()
    {
        // Has a claim, but it's Email not NameIdentifier
        var claims = new[] { new Claim(ClaimTypes.Email, "alice@example.com") };
        var user = new User { UserId = UserId.New(), UserName = "alice@example.com" };
        var service = BuildService(AuthState(claims), [user]);

        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_EmptyUserList_ReturnsDefault()
    {
        var service = BuildService(AuthState("alice@example.com"), []);

        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_EmptyNameIdentifier_ReturnsDefault()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "") };
        var user = new User { UserId = UserId.New(), UserName = "" };
        var service = BuildService(AuthState(claims), [user]);

        // Empty string username lookup — service behaviour: returns matched user
        // (same as any other username match)
        var result = await service.GetCurrentUserIdAsync();

        Assert.Equal(user.UserId, result);
    }

    #endregion

    #region Test doubles

    private sealed class FakeAuthStateProvider(AuthenticationState state)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(state);
    }

    /// <summary>
    /// Overrides the Marten LINQ query with an in-memory lookup.
    /// Marten's SingleOrDefaultAsync hard-casts to its internal MartenLinqQueryable,
    /// making the DB query non-mockable at the unit level; this subclass bypasses it.
    /// </summary>
    private sealed class TestableCurrentUserService(
        AuthenticationStateProvider authProvider,
        IQuerySession session,
        List<User> users
    ) : CurrentUserService(authProvider, session)
    {
        protected override Task<UserId> QueryUserIdByNameAsync(string? userName) =>
            Task.FromResult(
                users.Where(u => u.UserName == userName).Select(u => u.UserId).SingleOrDefault()
            );
    }

    #endregion
}
