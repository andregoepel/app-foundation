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
        Render(BuildUserManager(), new DefaultHttpContext());

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    #endregion

    #region User not found

    [Fact]
    public void UserNotFound_Sets404StatusCode()
    {
        var userManager = BuildUserManager();
        userManager.FindByIdAsync("missing").Returns(Task.FromResult<User?>(null));

        var httpContext = new DefaultHttpContext();
        Render(userManager, httpContext, userId: "missing", code: "token");

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public void UserNotFound_ShowsUserIdInErrorMessage()
    {
        var userManager = BuildUserManager();
        userManager.FindByIdAsync("missing").Returns(Task.FromResult<User?>(null));

        var cut = Render(userManager, new DefaultHttpContext(), userId: "missing", code: "token");

        Assert.Contains("missing", cut.Markup);
    }

    #endregion

    #region Confirmation fails

    [Fact]
    public void ConfirmationFailed_ShowsErrorMessage()
    {
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ConfirmEmailAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Failed()));

        var cut = Render(userManager, new DefaultHttpContext(), userId: "test", code: "bad-token");

        Assert.Contains("Error confirming your email", cut.Markup);
    }

    #endregion

    #region Confirmation succeeds

    [Fact]
    public void ConfirmationSucceeded_ShowsSuccessMessage()
    {
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ConfirmEmailAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var cut = Render(userManager, new DefaultHttpContext(), userId: "test", code: "good-token");

        Assert.Contains("Thank you for confirming your email", cut.Markup);
    }

    [Fact]
    public void ConfirmationSucceeded_ShowsLoginButton()
    {
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ConfirmEmailAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var cut = Render(userManager, new DefaultHttpContext(), userId: "test", code: "good-token");

        Assert.Contains("Log in", cut.Markup);
    }

    #endregion
}
