using AndreGoepel.AppFoundation.Components.Pages;
using AndreGoepel.AppFoundation.Tests.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using AppErrorPage = AndreGoepel.AppFoundation.Components.Shared.ErrorPage;
using WebsiteErrorPage = AndreGoepel.Website.Components.Shared.ErrorPage;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

public class NotFoundPageTests : ErrorComponentContext
{
    [Fact]
    public void Render_WhenAuthenticated_ShowsFoundationErrorPage()
    {
        // Arrange
        AddAuthorization().SetAuthorized("alice");

        // Act
        var cut = Render<NotFoundPage>();

        // Assert
        Assert.Single(cut.FindComponents<AppErrorPage>());
        Assert.Empty(cut.FindComponents<WebsiteErrorPage>());
        Assert.Contains("Page not found", cut.Markup);
    }

    [Fact]
    public void Render_WhenAnonymous_ShowsWebsiteErrorPage()
    {
        // Arrange
        AddAuthorization().SetNotAuthorized();

        // Act
        var cut = Render<NotFoundPage>();

        // Assert
        Assert.Single(cut.FindComponents<WebsiteErrorPage>());
        Assert.Empty(cut.FindComponents<AppErrorPage>());
        Assert.Equal("404", cut.Find(".err-terminal-status").TextContent);
    }

    [Fact]
    public void Route_IsCatchAll()
    {
        // Act
        var route = Attribute.GetCustomAttribute(typeof(NotFoundPage), typeof(RouteAttribute));

        // Assert
        Assert.Equal("/{*url}", Assert.IsType<RouteAttribute>(route).Template);
    }
}
