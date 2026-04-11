using AndreGoepel.MembersArea.Components.Account.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.MembersArea.Tests.Account.Shared;

public class RedirectToLoginTests : BunitContext
{
    #region Redirect behaviour

    [Fact]
    public void OnInitialized_RedirectsToLoginPage()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/protected-page");

        Render<RedirectToLogin>();

        Assert.StartsWith("http://localhost/Account/Login", nav.Uri);
    }

    [Fact]
    public void OnInitialized_IncludesReturnUrlInRedirect()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/protected-page");

        Render<RedirectToLogin>();

        Assert.Contains("returnUrl", nav.Uri);
        Assert.Contains("protected-page", Uri.UnescapeDataString(nav.Uri));
    }

    #endregion
}
