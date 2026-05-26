using System.Security.Claims;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Components.Authorization;

namespace AndreGoepel.Marten.Identity.Tests.Services;

public class CurrentUserServiceTests
{
    #region Helpers

    private static AuthenticationState AuthState(params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test")));

    private static AuthenticationState UnauthenticatedState() => new(new ClaimsPrincipal());

    private static CurrentUserService Build(AuthenticationState state) =>
        new(new FakeAuthStateProvider(state));

    #endregion

    #region Tests

    [Fact]
    public async Task GetCurrentUserIdAsync_ValidGuidNameIdentifier_ReturnsParsedUserId()
    {
        // Arrange
        var expected = UserId.New();
        var service = Build(AuthState(new Claim(ClaimTypes.NameIdentifier, expected.ToString())));

        // Act
        var result = await service.GetCurrentUserIdAsync();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_UnauthenticatedPrincipal_ReturnsDefault()
    {
        // Arrange
        var service = Build(UnauthenticatedState());

        // Act
        var result = await service.GetCurrentUserIdAsync();

        // Assert
        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_OnlyEmailClaim_ReturnsDefault()
    {
        // Arrange — has a claim, but not NameIdentifier
        var service = Build(AuthState(new Claim(ClaimTypes.Email, "alice@example.com")));

        // Act
        var result = await service.GetCurrentUserIdAsync();

        // Assert
        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_NonGuidNameIdentifier_ReturnsDefault()
    {
        // Arrange
        var service = Build(AuthState(new Claim(ClaimTypes.NameIdentifier, "not-a-guid")));

        // Act
        var result = await service.GetCurrentUserIdAsync();

        // Assert
        Assert.Equal(default, result);
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_EmptyNameIdentifier_ReturnsDefault()
    {
        // Arrange
        var service = Build(AuthState(new Claim(ClaimTypes.NameIdentifier, "")));

        // Act
        var result = await service.GetCurrentUserIdAsync();

        // Assert
        Assert.Equal(default, result);
    }

    #endregion

    #region Test doubles

    private sealed class FakeAuthStateProvider(AuthenticationState state)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(state);
    }

    #endregion
}
