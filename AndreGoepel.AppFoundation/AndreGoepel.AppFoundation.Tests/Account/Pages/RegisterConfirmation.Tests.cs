using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.MembersArea.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AndreGoepel.MembersArea.Tests.Account.Pages;

public class RegisterConfirmationTests : BunitContext
{
    #region Helpers

    private static UserManager<User> BuildUserManager()
    {
        var store = Substitute.For<IUserStore<User>>();
        return Substitute.For<UserManager<User>>(
            store,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
    }

    private IRenderedComponent<RegisterConfirmation> Render(
        UserManager<User> userManager,
        string? email = null
    )
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(userManager);

        var nav = Services.GetRequiredService<NavigationManager>();
        var url =
            "/Account/RegisterConfirmation"
            + (email is not null ? $"?Email={Uri.EscapeDataString(email)}" : "");
        nav.NavigateTo(url);

        return Render<RegisterConfirmation>(p => p.AddCascadingValue(new DefaultHttpContext()));
    }

    #endregion

    #region Missing email

    [Fact]
    public void MissingEmail_RedirectsToRoot()
    {
        // Arrange / Act
        Render(BuildUserManager(), email: null);

        // Assert
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    #endregion

    #region User found

    [Fact]
    public void UserFound_ShowsConfirmationMessage()
    {
        // Arrange
        var user = new User { UserId = UserId.New(), UserName = "alice@example.com" };
        var userManager = BuildUserManager();
        userManager.FindByEmailAsync("alice@example.com").Returns(Task.FromResult<User?>(user));

        // Arrange / Act
        var cut = Render(userManager, email: "alice@example.com");

        // Assert
        Assert.Contains("check your email", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region User not found

    [Fact]
    public void UserNotFound_ShowsErrorMessage()
    {
        // Arrange
        var userManager = BuildUserManager();
        userManager.FindByEmailAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(null));

        // Arrange / Act
        var cut = Render(userManager, email: "unknown@example.com");

        // Assert
        Assert.Contains("Error finding user", cut.Markup);
    }

    [Fact]
    public void UserNotFound_Sets404StatusCode()
    {
        // Arrange
        var userManager = BuildUserManager();
        userManager.FindByEmailAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(null));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(userManager);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/Account/RegisterConfirmation?Email=unknown%40example.com");
        var httpContext = new DefaultHttpContext();

        // Act
        Render<RegisterConfirmation>(p => p.AddCascadingValue(httpContext));

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    #endregion
}
