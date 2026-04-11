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
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Arrange / Act
        var cut = Render<AccessDenied>();

        // Assert
        Assert.Contains("Access denied", cut.Markup);
    }

    [Fact]
    public void AccessDenied_GoToHomeButton_NavigatesToRoot()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<AccessDenied>();

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/", Nav.Uri);
    }

    #endregion

    #region Lockout

    [Fact]
    public void Lockout_RendersLockoutMessage()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Arrange / Act
        var cut = Render<Lockout>();

        // Assert
        Assert.Contains("locked out", cut.Markup);
    }

    [Fact]
    public void Lockout_BackToLoginButton_NavigatesToLogin()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<Lockout>();

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/Account/Login", Nav.Uri);
    }

    #endregion

    #region InvalidUser

    [Fact]
    public void InvalidUser_RendersErrorMessage()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Arrange / Act
        var cut = Render<InvalidUser>();

        // Assert
        Assert.Contains("could not be loaded", cut.Markup);
    }

    [Fact]
    public void InvalidUser_GoToHomeButton_NavigatesToRoot()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<InvalidUser>();

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/", Nav.Uri);
    }

    #endregion

    #region ForgotPasswordConfirmation

    [Fact]
    public void ForgotPasswordConfirmation_RendersCheckEmailMessage()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Arrange / Act
        var cut = Render<ForgotPasswordConfirmation>();

        // Assert
        Assert.Contains("password reset link has been sent", cut.Markup);
    }

    [Fact]
    public void ForgotPasswordConfirmation_BackToLoginButton_NavigatesToLogin()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<ForgotPasswordConfirmation>();

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/Account/Login", Nav.Uri);
    }

    #endregion

    #region ResetPasswordConfirmation

    [Fact]
    public void ResetPasswordConfirmation_RendersSuccessMessage()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Arrange / Act
        var cut = Render<ResetPasswordConfirmation>();

        // Assert
        Assert.Contains("reset successfully", cut.Markup);
    }

    [Fact]
    public void ResetPasswordConfirmation_LoginButton_NavigatesToLogin()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<ResetPasswordConfirmation>();

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/Account/Login", Nav.Uri);
    }

    #endregion
}
