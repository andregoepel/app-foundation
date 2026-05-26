using AndreGoepel.Marten.Identity.Blazor.Components.Account.Pages.Manage;
using AndreGoepel.Marten.Identity.Blazor.Tests.TestSupport;
using AndreGoepel.Marten.Identity.Users;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Radzen;

namespace AndreGoepel.Marten.Identity.Blazor.Tests.Account.Pages.Manage;

public class ChangePasswordTests : BunitContext
{
    #region Helpers

    private (IRenderedComponent<ChangePassword> Cut, UserManager<User> Um, User CurrentUser) Render(
        bool hasPassword = true,
        Action<UserManager<User>, User>? configure = null
    )
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var user = new User { Email = "alice@example.com" };
        var um = AuthenticatedUserContext.BuildUserManager();
        var (auth, principal) = AuthenticatedUserContext.BuildAuthState(user);
        um.GetUserAsync(principal).Returns(user);
        um.HasPasswordAsync(user).Returns(hasPassword);
        configure?.Invoke(um, user);

        Services.AddSingleton(auth);
        Services.AddSingleton(um);
        Services.AddSingleton(Substitute.For<ILogger<ChangePassword>>());
        Services.AddSingleton(new NotificationService());
        return (Render<ChangePassword>(), um, user);
    }

    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private static async Task SubmitAsync(IRenderedComponent<ChangePassword> cut)
    {
        cut.Find("input[name=OldPassword]").Change("old");
        cut.Find("input[name=NewPassword]").Change("Br@ndNewPw1");
        cut.Find("input[name=ConfirmPassword]").Change("Br@ndNewPw1");
        await cut.Find("form").SubmitAsync();
    }

    #endregion

    [Fact]
    public void RendersThreePasswordInputs()
    {
        // Arrange / Act
        var (cut, _, _) = Render();

        // Assert
        Assert.NotNull(cut.Find("input[name=OldPassword]"));
        Assert.NotNull(cut.Find("input[name=NewPassword]"));
        Assert.NotNull(cut.Find("input[name=ConfirmPassword]"));
    }

    [Fact]
    public void OnInit_NoPassword_RedirectsToSetPassword()
    {
        // Arrange / Act
        Render(hasPassword: false);

        // Assert
        Assert.Contains("Account/Manage/SetPassword", Nav.Uri);
    }

    [Fact]
    public async Task Submit_Successful_ShowsSuccessNotification()
    {
        // Arrange
        var (cut, um, user) = Render(
            configure: (um, user) =>
                um.ChangePasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
                    .Returns(IdentityResult.Success)
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Success);
    }

    [Fact]
    public async Task Submit_Fails_ShowsErrorNotification()
    {
        // Arrange
        var (cut, um, user) = Render(
            configure: (um, user) =>
                um.ChangePasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
                    .Returns(IdentityResult.Failed(new IdentityError { Description = "weak" }))
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Error);
    }
}
