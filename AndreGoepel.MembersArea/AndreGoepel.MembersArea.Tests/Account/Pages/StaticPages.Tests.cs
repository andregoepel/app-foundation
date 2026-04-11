using AndreGoepel.MembersArea.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.MembersArea.Tests.Account.Pages;

public class StaticPagesTests : BunitContext
{
    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    #region AccessDenied

    [Fact]
    public void AccessDenied_RendersAccessDeniedMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<AccessDenied>();

        Assert.Contains("Access denied", cut.Markup);
    }

    [Fact]
    public void AccessDenied_GoToHomeButton_NavigatesToRoot()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<AccessDenied>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/", Nav.Uri);
    }

    #endregion

    #region Lockout

    [Fact]
    public void Lockout_RendersLockoutMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<Lockout>();

        Assert.Contains("locked out", cut.Markup);
    }

    [Fact]
    public void Lockout_BackToLoginButton_NavigatesToLogin()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<Lockout>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/Account/Login", Nav.Uri);
    }

    #endregion

    #region InvalidUser

    [Fact]
    public void InvalidUser_RendersErrorMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<InvalidUser>();

        Assert.Contains("could not be loaded", cut.Markup);
    }

    [Fact]
    public void InvalidUser_GoToHomeButton_NavigatesToRoot()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<InvalidUser>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/", Nav.Uri);
    }

    #endregion

    #region ForgotPasswordConfirmation

    [Fact]
    public void ForgotPasswordConfirmation_RendersCheckEmailMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ForgotPasswordConfirmation>();

        Assert.Contains("password reset link has been sent", cut.Markup);
    }

    [Fact]
    public void ForgotPasswordConfirmation_BackToLoginButton_NavigatesToLogin()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ForgotPasswordConfirmation>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/Account/Login", Nav.Uri);
    }

    #endregion

    #region ResetPasswordConfirmation

    [Fact]
    public void ResetPasswordConfirmation_RendersSuccessMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ResetPasswordConfirmation>();

        Assert.Contains("reset successfully", cut.Markup);
    }

    [Fact]
    public void ResetPasswordConfirmation_LoginButton_NavigatesToLogin()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ResetPasswordConfirmation>();
        cut.Find("button").Click();

        Assert.Equal("http://localhost/Account/Login", Nav.Uri);
    }

    #endregion
}
