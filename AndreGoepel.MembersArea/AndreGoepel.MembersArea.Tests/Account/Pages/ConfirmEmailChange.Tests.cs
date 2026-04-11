using System.Text;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.MembersArea.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AndreGoepel.MembersArea.Tests.Account.Pages;

public class ConfirmEmailChangeTests : BunitContext
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

    private static SignInManager<User> BuildSignInManager(UserManager<User> userManager)
    {
        return Substitute.For<SignInManager<User>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<User>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<User>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<User>>()
        );
    }

    private static string Encode(string value) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));

    private IRenderedComponent<ConfirmEmailChange> Render(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        string? userId = null,
        string? email = null,
        string? code = null
    )
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(userManager);
        Services.AddSingleton(signInManager);

        var nav = Services.GetRequiredService<NavigationManager>();
        var query = new List<string>();
        if (userId is not null)
            query.Add($"UserId={Uri.EscapeDataString(userId)}");
        if (email is not null)
            query.Add($"Email={Uri.EscapeDataString(email)}");
        if (code is not null)
            query.Add($"Code={Uri.EscapeDataString(Encode(code))}");
        nav.NavigateTo(
            "/Account/ConfirmEmailChange" + (query.Count > 0 ? "?" + string.Join("&", query) : "")
        );

        return Render<ConfirmEmailChange>();
    }

    #endregion

    #region Missing parameters

    [Fact]
    public void MissingParameters_ShowsInvalidLinkError()
    {
        var userManager = BuildUserManager();
        var cut = Render(userManager, BuildSignInManager(userManager));

        Assert.Contains("Invalid email change confirmation link", cut.Markup);
    }

    #endregion

    #region User not found

    [Fact]
    public void UserNotFound_ShowsUserIdInError()
    {
        var userManager = BuildUserManager();
        userManager.FindByIdAsync("missing").Returns(Task.FromResult<User?>(null));

        var cut = Render(
            userManager,
            BuildSignInManager(userManager),
            userId: "missing",
            email: "new@example.com",
            code: "token"
        );

        Assert.Contains("missing", cut.Markup);
    }

    #endregion

    #region Change email fails

    [Fact]
    public void ChangeEmailFailed_ShowsErrorMessage()
    {
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ChangeEmailAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Failed()));

        var cut = Render(
            userManager,
            BuildSignInManager(userManager),
            userId: "test",
            email: "new@example.com",
            code: "bad-token"
        );

        Assert.Contains("Error changing email", cut.Markup);
    }

    #endregion

    #region Change email succeeds

    [Fact]
    public void ChangeEmailSucceeded_ShowsSuccessMessage()
    {
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ChangeEmailAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));
        userManager
            .SetUserNameAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var cut = Render(
            userManager,
            BuildSignInManager(userManager),
            userId: "test",
            email: "new@example.com",
            code: "good-token"
        );

        Assert.Contains("Thank you for confirming your email change", cut.Markup);
    }

    [Fact]
    public void ChangeEmailSucceeded_ShowsGoToProfileButton()
    {
        var user = new User { UserId = UserId.New() };
        var userManager = BuildUserManager();
        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult<User?>(user));
        userManager
            .ChangeEmailAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));
        userManager
            .SetUserNameAsync(user, Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var cut = Render(
            userManager,
            BuildSignInManager(userManager),
            userId: "test",
            email: "new@example.com",
            code: "good-token"
        );

        Assert.Contains("Go to profile", cut.Markup);
    }

    #endregion
}
