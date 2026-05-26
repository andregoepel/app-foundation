using AndreGoepel.Marten.Identity.Blazor.Components.Account.Pages;
using AndreGoepel.Marten.Identity.Users;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Radzen;

namespace AndreGoepel.Marten.Identity.Blazor.Tests.Account.Pages;

public class ForgotPasswordTests : BunitContext
{
    #region Helpers

    private (
        IRenderedComponent<ForgotPassword> Cut,
        UserManager<User> Um,
        IEmailSender<User> Email
    ) Render(Action<UserManager<User>>? configure = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var store = Substitute.For<IUserStore<User>>();
        var um = Substitute.For<UserManager<User>>(
            store,
            Options.Create(new IdentityOptions()),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!
        );
        configure?.Invoke(um);

        var emailSender = Substitute.For<IEmailSender<User>>();
        Services.AddSingleton(um);
        Services.AddSingleton(emailSender);
        Services.AddSingleton(new NotificationService());
        return (Render<ForgotPassword>(), um, emailSender);
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private static async Task SubmitAsync(
        IRenderedComponent<ForgotPassword> cut,
        string email = "alice@example.com"
    )
    {
        cut.Find("input[name=Email]").Change(email);
        await cut.Find("form").SubmitAsync();
    }

    #endregion

    [Fact]
    public void RendersEmailInput()
    {
        // Arrange / Act
        var (cut, _, _) = Render();

        // Assert
        Assert.NotNull(cut.Find("input[name=Email]"));
    }

    [Fact]
    public async Task Submit_UnknownEmail_NavigatesToConfirmation_WithoutSendingEmail()
    {
        // Arrange
        var (cut, um, email) = Render(um =>
            um.FindByEmailAsync(Arg.Any<string>()).Returns((User?)null)
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Contains("Account/ForgotPasswordConfirmation", Nav.Uri);
        await email
            .DidNotReceive()
            .SendPasswordResetLinkAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Submit_UnconfirmedEmail_NavigatesToConfirmation_WithoutSendingEmail()
    {
        // Arrange
        var user = new User { Email = "alice@example.com" };
        var (cut, um, email) = Render(um =>
        {
            um.FindByEmailAsync(Arg.Any<string>()).Returns(user);
            um.IsEmailConfirmedAsync(user).Returns(false);
        });

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Contains("Account/ForgotPasswordConfirmation", Nav.Uri);
        await email
            .DidNotReceive()
            .SendPasswordResetLinkAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Submit_ConfirmedUser_SendsResetLink_AndNavigates()
    {
        // Arrange
        var user = new User { Email = "alice@example.com" };
        var (cut, um, email) = Render(um =>
        {
            um.FindByEmailAsync(Arg.Any<string>()).Returns(user);
            um.IsEmailConfirmedAsync(user).Returns(true);
            um.GeneratePasswordResetTokenAsync(user).Returns("token");
        });

        // Act
        await SubmitAsync(cut);

        // Assert
        await email
            .Received(1)
            .SendPasswordResetLinkAsync(user, "alice@example.com", Arg.Any<string>());
        Assert.Contains("Account/ForgotPasswordConfirmation", Nav.Uri);
    }
}
