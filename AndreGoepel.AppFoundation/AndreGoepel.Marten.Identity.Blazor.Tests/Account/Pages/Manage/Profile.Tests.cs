using AndreGoepel.Marten.Identity.Blazor.Components.Account.Pages.Manage;
using AndreGoepel.Marten.Identity.Blazor.Tests.TestSupport;
using AndreGoepel.Marten.Identity.Users;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.Marten.Identity.Blazor.Tests.Account.Pages.Manage;

public class ProfileTests : BunitContext
{
    #region Helpers

    private (IRenderedComponent<Profile> Cut, UserManager<User> Um, User CurrentUser) Render(
        string currentPhone = "+49 000 0000000",
        Action<UserManager<User>, User>? configure = null
    )
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var user = new User
        {
            Email = "alice@example.com",
            UserName = "alice",
            PhoneNumber = currentPhone,
        };
        var um = AuthenticatedUserContext.BuildUserManager();
        var (auth, principal) = AuthenticatedUserContext.BuildAuthState(user);
        um.GetUserAsync(principal).Returns(user);
        configure?.Invoke(um, user);

        Services.AddSingleton(auth);
        Services.AddSingleton(um);
        Services.AddSingleton(new NotificationService());
        return (Render<Profile>(), um, user);
    }

    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    #endregion

    [Fact]
    public void RendersUserNameAndPhoneNumberInputs()
    {
        // Arrange / Act
        var (cut, _, _) = Render();

        // Assert
        Assert.NotNull(cut.Find("input[name=Username]"));
        Assert.NotNull(cut.Find("input[name=PhoneNumber]"));
    }

    [Fact]
    public async Task Submit_UnchangedPhone_ShowsInfoNotification()
    {
        // Arrange
        var (cut, _, _) = Render(currentPhone: "+49 123 4567890");
        cut.Find("input[name=PhoneNumber]").Change("+49 123 4567890");

        // Act
        await cut.Find("form").SubmitAsync();

        // Assert
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Info);
    }

    [Fact]
    public async Task Submit_PhoneChanged_CallsSetPhoneNumber_AndNotifiesSuccess()
    {
        // Arrange
        var (cut, um, user) = Render(
            currentPhone: "+49 123 4567890",
            configure: (um, user) =>
                um.SetPhoneNumberAsync(user, Arg.Any<string>()).Returns(IdentityResult.Success)
        );
        cut.Find("input[name=PhoneNumber]").Change("+49 999 9999999");

        // Act
        await cut.Find("form").SubmitAsync();

        // Assert
        await um.Received(1).SetPhoneNumberAsync(user, Arg.Is<string>(p => p == "+49 999 9999999"));
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Success);
    }

    [Fact]
    public async Task Submit_SetPhoneFails_NotifiesError()
    {
        // Arrange
        var (cut, um, user) = Render(
            currentPhone: "+49 123 4567890",
            configure: (um, user) =>
                um.SetPhoneNumberAsync(user, Arg.Any<string>())
                    .Returns(IdentityResult.Failed(new IdentityError { Description = "bad" }))
        );
        cut.Find("input[name=PhoneNumber]").Change("+49 999 9999999");

        // Act
        await cut.Find("form").SubmitAsync();

        // Assert
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Error);
    }
}
