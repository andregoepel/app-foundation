using AndreGoepel.AppFoundation.Components.Account.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Account.Shared;

public class RedirectToLoginTests : BunitContext
{
    #region Redirect behaviour

    [Fact]
    public void OnInitialized_RedirectsToLoginPage()
    {
        // Arrange
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/protected-page");

        // Act
        Render<RedirectToLogin>();

        // Assert
        Assert.StartsWith("http://localhost/Account/Login", nav.Uri);
    }

    [Fact]
    public void OnInitialized_IncludesReturnUrlInRedirect()
    {
        // Arrange
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/protected-page");

        // Act
        Render<RedirectToLogin>();

        // Assert
        Assert.Contains("returnUrl", nav.Uri);
        Assert.Contains("protected-page", Uri.UnescapeDataString(nav.Uri));
    }

    #endregion
}
