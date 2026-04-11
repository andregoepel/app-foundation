using AndreGoepel.MembersArea.Components.Account.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace AndreGoepel.MembersArea.Tests.Account.Pages;

public class LoginWithRecoveryCodeTests : BunitContext
{
    #region Helpers

    private IRenderedComponent<LoginWithRecoveryCode> Render(
        string? error = null,
        string? returnUrl = null
    )
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
            "/Account/LoginWithRecoveryCode"
                + (query.Count > 0 ? "?" + string.Join("&", query) : "")
        );

        return Render<LoginWithRecoveryCode>();
    }

    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    #endregion

    #region Error query param

    [Fact]
    public void WithErrorInvalid_ShowsErrorNotification()
    {
        Render(error: "invalid");

        Assert.Single(Notifications.Messages);
    }

    [Fact]
    public void WithErrorInvalid_NotificationHasCorrectSeverity()
    {
        Render(error: "invalid");

        Assert.Equal(NotificationSeverity.Error, Notifications.Messages[0].Severity);
    }

    [Fact]
    public void WithoutError_NoNotification()
    {
        Render();

        Assert.Empty(Notifications.Messages);
    }

    #endregion

    #region Rendering

    [Fact]
    public void RendersRecoveryCodeInput()
    {
        var cut = Render();

        Assert.Contains("Recovery code", cut.Markup);
    }

    [Fact]
    public void RendersLinkToAuthenticatorLogin()
    {
        var cut = Render();

        Assert.Contains("authenticator", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
