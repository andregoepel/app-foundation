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

public class RegisterTests : BunitContext
{
    #region Helpers

    private (
        IRenderedComponent<Register> Cut,
        UserManager<User> Um,
        IUserStore<User> Store,
        IEmailSender<User> Email
    ) Render(Action<UserManager<User>>? configure = null, bool requireConfirmedAccount = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var store = Substitute.For<IUserStore<User>, IUserEmailStore<User>>();
        var identityOptions = Options.Create(
            new IdentityOptions { SignIn = { RequireConfirmedAccount = requireConfirmedAccount } }
        );
        var um = Substitute.For<UserManager<User>>(
            store,
            identityOptions,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!
        );
        um.SupportsUserEmail.Returns(true);
        configure?.Invoke(um);

        var emailSender = Substitute.For<IEmailSender<User>>();
        var signInMgr = Substitute.For<SignInManager<User>>(
            um,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<User>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<User>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<User>>()
        );

        Services.AddSingleton(um);
        Services.AddSingleton(store);
        Services.AddSingleton(signInMgr);
        Services.AddSingleton(emailSender);
        Services.AddSingleton(Substitute.For<ILogger<Register>>());
        Services.AddSingleton(new NotificationService());
        Services.AddSingleton(new LoginTokenProtector(DataProtectionProvider.Create("Tests")));

        return (Render<Register>(), um, store, emailSender);
    }

    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private static async Task SubmitAsync(
        IRenderedComponent<Register> cut,
        string email = "alice@example.com",
        string password = "P@ssw0rd!"
    )
    {
        cut.Find("input[name=Email]").Change(email);
        cut.Find("input[name=NewPassword]").Change(password);
        cut.Find("input[name=ConfirmPassword]").Change(password);
        await cut.Find("form").SubmitAsync();
    }

    #endregion

    [Fact]
    public void RendersEmailPasswordConfirmFields()
    {
        // Arrange / Act
        var (cut, _, _, _) = Render();

        // Assert
        Assert.NotNull(cut.Find("input[name=Email]"));
        Assert.NotNull(cut.Find("input[name=NewPassword]"));
        Assert.NotNull(cut.Find("input[name=ConfirmPassword]"));
    }

    [Fact]
    public async Task Submit_SuccessfulCreate_WithRequireConfirmed_SendsEmailAndNavigatesToConfirmation()
    {
        // Arrange
        var (cut, um, _, email) = Render(um =>
        {
            um.CreateAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);
            um.GetUserIdAsync(Arg.Any<User>()).Returns("uid");
            um.GenerateEmailConfirmationTokenAsync(Arg.Any<User>()).Returns("token");
        });

        // Act
        await SubmitAsync(cut);

        // Assert
        await email
            .Received(1)
            .SendConfirmationLinkAsync(Arg.Any<User>(), "alice@example.com", Arg.Any<string>());
        Assert.Contains("Account/RegisterConfirmation", Nav.Uri);
    }

    [Fact]
    public async Task Submit_CreateFails_ShowsErrorNotification()
    {
        // Arrange
        var (cut, um, _, _) = Render(um =>
            um.CreateAsync(Arg.Any<User>(), Arg.Any<string>())
                .Returns(IdentityResult.Failed(new IdentityError { Description = "duplicate" }))
        );

        // Act
        await SubmitAsync(cut);

        // Assert
        Assert.Single(Notifications.Messages);
        Assert.Equal(NotificationSeverity.Error, Notifications.Messages[0].Severity);
    }
}
