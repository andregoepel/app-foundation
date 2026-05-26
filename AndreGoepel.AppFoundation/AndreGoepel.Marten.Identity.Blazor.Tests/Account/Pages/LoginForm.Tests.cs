using AndreGoepel.Marten.Identity.Blazor.Components.Account.Pages;
using AndreGoepel.Marten.Identity.Http;
using AndreGoepel.Marten.Identity.Users;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Radzen;

namespace AndreGoepel.Marten.Identity.Blazor.Tests.Account.Pages;

public class LoginFormTests : BunitContext
{
    #region Helpers

    private static UserManager<User> BuildUserManager() =>
        Substitute.For<UserManager<User>>(
            Substitute.For<IUserStore<User>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

    private static SignInManager<User> BuildSignInManager(UserManager<User> userManager) =>
        Substitute.For<SignInManager<User>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<User>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<User>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<User>>()
        );

    private (
        IRenderedComponent<LoginForm> Cut,
        UserManager<User> Um,
        SignInManager<User> Sm
    ) Render(Action<UserManager<User>, SignInManager<User>>? configure = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var um = BuildUserManager();
        var sm = BuildSignInManager(um);
        configure?.Invoke(um, sm);
        Services.AddSingleton(um);
        Services.AddSingleton(sm);
        Services.AddSingleton(new NotificationService());
        Services.AddSingleton(Substitute.For<ILogger<Login>>());
        Services.AddSingleton(new LoginTokenProtector(DataProtectionProvider.Create("Tests")));
        var cut = Render<LoginForm>();
        return (cut, um, sm);
    }

    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private async Task<IRenderedComponent<LoginForm>> SubmitAsync(
        IRenderedComponent<LoginForm> cut,
        string email = "alice@example.com",
        string password = "pw"
    )
    {
        cut.Find("input[name=Email]").Change(email);
        cut.Find("input[name=Password]").Change(password);
        await cut.Find("form").SubmitAsync();
        return cut;
    }

    #endregion

    [Fact]
    public void RendersEmailPasswordRememberMeAndSubmit()
    {
        // Arrange / Act
        var (cut, _, _) = Render();

        // Assert
        Assert.NotNull(cut.Find("input[name=Email]"));
        Assert.NotNull(cut.Find("input[name=Password]"));
        Assert.NotNull(cut.Find("input[name=RememberMe]"));
    }

    [Fact]
    public async Task Submit_UnknownEmail_ShowsErrorNotification_AndDoesNotNavigate()
    {
        // Arrange
        var startUri = "http://localhost/";
        var (cut, um, _) = Render(
            (um, sm) => um.FindByEmailAsync(Arg.Any<string>()).Returns((User?)null)
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Single(Notifications.Messages);
        Assert.Equal(startUri, Nav.Uri);
    }

    [Fact]
    public async Task Submit_LockedOutUser_NavigatesToLockout()
    {
        // Arrange
        var user = new User { Email = "alice@example.com" };
        var (cut, um, sm) = Render(
            (um, sm) =>
            {
                um.FindByEmailAsync(Arg.Any<string>()).Returns(user);
                sm.CanSignInAsync(user).Returns(true);
                um.IsLockedOutAsync(user).Returns(true);
            }
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.EndsWith("Account/Lockout", Nav.Uri);
    }

    [Fact]
    public async Task Submit_WrongPassword_NotifiesAndCallsAccessFailed()
    {
        // Arrange
        var user = new User { Email = "alice@example.com" };
        var (cut, um, sm) = Render(
            (um, sm) =>
            {
                um.FindByEmailAsync(Arg.Any<string>()).Returns(user);
                sm.CanSignInAsync(user).Returns(true);
                um.IsLockedOutAsync(user).Returns(false);
                um.CheckPasswordAsync(user, Arg.Any<string>()).Returns(false);
            }
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Single(Notifications.Messages);
        await um.Received(1).AccessFailedAsync(user);
    }

    [Fact]
    public async Task Submit_ValidCreds_NavigatesToLoginWithToken()
    {
        // Arrange
        var user = new User { Email = "alice@example.com" };
        var (cut, um, sm) = Render(
            (um, sm) =>
            {
                um.FindByEmailAsync(Arg.Any<string>()).Returns(user);
                sm.CanSignInAsync(user).Returns(true);
                um.IsLockedOutAsync(user).Returns(false);
                um.CheckPasswordAsync(user, Arg.Any<string>()).Returns(true);
            }
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Contains("/login?token=", Nav.Uri);
    }
}
