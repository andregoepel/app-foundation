using System.Text;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.MembersArea.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AndreGoepel.MembersArea.Tests.Account.Pages;

public class ConfirmEmailTests : BunitContext
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

    private static string Encode(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private IRenderedComponent<ConfirmEmail> Render(
        UserManager<User> userManager,
        HttpContext httpContext,
        string? userId = null,
        string? code = null
    )
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(userManager);

        var nav = Services.GetRequiredService<NavigationManager>();
        var query = new List<string>();
        if (userId is not null)
            query.Add($"UserId={Uri.EscapeDataString(userId)}");
        if (code is not null)
            query.Add($"Code={Uri.EscapeDataString(Encode(code))}");
        nav.NavigateTo(
            "/Account/ConfirmEmail" + (query.Count > 0 ? "?" + string.Join("&", query) : "")
        );

        return Render<ConfirmEmail>(p => p.AddCascadingValue(httpContext));
    }

    #endregion

    #region Missing parameters

    [Fact]
    public void MissingParameters_RedirectsToRoot()
    {
        // Arrange / Act
        Render(BuildUserManager(), new DefaultHttpContext());

        // Assert
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    #endregion

    #region User not found

    [Fact]
    public void UserNotFound_Sets404StatusCode()
    {
        // Arrange
        var userManager = BuildUserManager();
        userManager.FindByIdAsync("missing").Returns(Task.FromResult<User?>(null));
        var httpContext = new DefaultHttpContext();

        // Act
        Render(userManager, httpContext, userId: "missing", code: "token");

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public void UserNotFound_ShowsUserIdInErrorMessage()
    {
        // Arrange
        var userManager = BuildUserManager();
        userManager.FindByIdAsync("missing").Returns(Task.FromResult<User?>(null));

        // Arrange / Act
        var cut = Render(userManager, new DefaultHttpContext(), userId: "missing", code: "token");

        // Assert
        Assert.Contains("missing", cut.Markup);
    }

    #endregion

    #region Confirmation fails

    [Fact]
    public void ConfirmationFailed_ShowsErrorMessage()
    {
        // Arrange
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ConfirmEmailAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Failed()));

        // Arrange / Act
        var cut = Render(userManager, new DefaultHttpContext(), userId: "test", code: "bad-token");

        // Assert
        Assert.Contains("Error confirming your email", cut.Markup);
    }

    #endregion

    #region Confirmation succeeds

    [Fact]
    public void ConfirmationSucceeded_ShowsSuccessMessage()
    {
        // Arrange
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ConfirmEmailAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        // Arrange / Act
        var cut = Render(userManager, new DefaultHttpContext(), userId: "test", code: "good-token");

        // Assert
        Assert.Contains("Thank you for confirming your email", cut.Markup);
    }

    [Fact]
    public void ConfirmationSucceeded_ShowsLoginButton()
    {
        // Arrange
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ConfirmEmailAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        // Arrange / Act
        var cut = Render(userManager, new DefaultHttpContext(), userId: "test", code: "good-token");

        // Assert
        Assert.Contains("Log in", cut.Markup);
    }

    #endregion
}
