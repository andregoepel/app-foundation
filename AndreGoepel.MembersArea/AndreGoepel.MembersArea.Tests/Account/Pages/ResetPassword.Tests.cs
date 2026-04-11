using System.Text;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.MembersArea.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.MembersArea.Tests.Account.Pages;

public class ResetPasswordTests : BunitContext
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

    private IRenderedComponent<ResetPassword> Render(UserManager<User> userManager, string? code)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(userManager);
        Services.AddSingleton(new NotificationService());

        var nav = Services.GetRequiredService<NavigationManager>();
        var url =
            "/Account/ResetPassword"
            + (code is not null ? $"?Code={Uri.EscapeDataString(Encode(code))}" : "");
        nav.NavigateTo(url);

        return Render<ResetPassword>();
    }

    #endregion

    #region Missing code

    [Fact]
    public void MissingCode_ShowsInvalidLinkAlert()
    {
        var cut = Render(BuildUserManager(), code: null);

        Assert.Contains("invalid", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingCode_ShowsRequestNewLinkButton()
    {
        var cut = Render(BuildUserManager(), code: null);

        Assert.Contains("Request new link", cut.Markup);
    }

    [Fact]
    public void MissingCode_RequestNewLinkButton_NavigatesToForgotPassword()
    {
        var cut = Render(BuildUserManager(), code: null);

        cut.Find("button").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/Account/ForgotPassword", nav.Uri);
    }

    #endregion

    #region Valid code

    [Fact]
    public void ValidCode_ShowsForm()
    {
        var cut = Render(BuildUserManager(), code: "valid-reset-token");

        Assert.Contains("Reset password", cut.Markup);
        Assert.DoesNotContain("invalid", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
