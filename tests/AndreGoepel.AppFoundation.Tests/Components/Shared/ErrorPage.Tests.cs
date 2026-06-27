using AndreGoepel.AppFoundation.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Components.Shared;

public class ErrorPageTests : BunitContext
{
    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ErrorPage> RenderError(string code)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ErrorPage>(p => p.Add(c => c.Code, code));
    }

    [Fact]
    public void Render_With404_ShowsNotFoundMessage()
    {
        // Act
        var cut = RenderError("404");

        // Assert
        Assert.Contains("Page not found", cut.Markup);
        Assert.Contains("has moved", cut.Markup);
    }

    [Fact]
    public void Render_With403_ShowsAccessDeniedMessage()
    {
        // Act
        var cut = RenderError("403");

        // Assert
        Assert.Contains("Access denied", cut.Markup);
        Assert.Contains("permission", cut.Markup);
    }

    [Fact]
    public void Render_WithUnknownCode_FallsBackToNotFound()
    {
        // Act
        var cut = RenderError("500");

        // Assert
        Assert.Contains("Page not found", cut.Markup);
    }

    [Fact]
    public void GoToHomeButton_NavigatesToRoot()
    {
        // Arrange
        var cut = RenderError("404");

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/", Nav.Uri);
    }
}
