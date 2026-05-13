using AndreGoepel.AppFoundation.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace AndreGoepel.AppFoundation.Tests.Account.Pages;

public class LoginWith2faTests : BunitContext
{
    #region Helpers

    private IRenderedComponent<LoginWith2fa> Render(string? error = null, string? returnUrl = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var notificationService = new NotificationService();
        Services.AddSingleton(notificationService);

        var nav = Services.GetRequiredService<NavigationManager>();
        var query = new List<string>();
        if (error is not null)
            query.Add($"Error={Uri.EscapeDataString(error)}");
        if (returnUrl is not null)
            query.Add($"ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        nav.NavigateTo(
            "/Account/LoginWith2fa" + (query.Count > 0 ? "?" + string.Join("&", query) : "")
        );

        return Render<LoginWith2fa>();
    }

    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    #endregion

    #region Error query param

    [Fact]
    public void WithErrorInvalid_ShowsErrorNotification()
    {
        // Arrange / Act
        Render(error: "invalid");

        // Assert
        Assert.Single(Notifications.Messages);
    }

    [Fact]
    public void WithErrorInvalid_NotificationHasCorrectSeverity()
    {
        // Arrange / Act
        Render(error: "invalid");

        // Assert
        Assert.Equal(NotificationSeverity.Error, Notifications.Messages[0].Severity);
    }

    [Fact]
    public void WithoutError_NoNotification()
    {
        // Arrange / Act
        Render();

        // Assert
        Assert.Empty(Notifications.Messages);
    }

    #endregion

    #region Rendering

    [Fact]
    public void RendersAuthenticatorCodeInput()
    {
        // Arrange / Act
        var cut = Render();

        // Assert
        Assert.Contains("Authenticator code", cut.Markup);
    }

    [Fact]
    public void RendersRecoveryCodeLink()
    {
        // Arrange / Act
        var cut = Render();

        // Assert
        Assert.Contains("recovery code", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
