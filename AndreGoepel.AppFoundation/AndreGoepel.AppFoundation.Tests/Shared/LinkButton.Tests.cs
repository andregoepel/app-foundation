using AndreGoepel.AppFoundation.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Shared;

public class LinkButtonTests : BunitContext
{
    #region Helpers

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<LinkButton> Render(string text, string path)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<LinkButton>(p => p.Add(c => c.Text, text).Add(c => c.Path, path));
    }

    #endregion

    #region Rendering

    [Fact]
    public void RendersButtonText()
    {
        // Arrange / Act
        var cut = Render("Go somewhere", "/somewhere");

        // Assert
        Assert.Contains("Go somewhere", cut.Markup);
    }

    #endregion

    #region Navigation

    [Fact]
    public void Click_NavigatesToPath()
    {
        // Arrange
        var cut = Render("Click me", "/target");

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/target", Nav.Uri);
    }

    [Fact]
    public void Click_NavigatesToCorrectPathWhenMultipleSegments()
    {
        // Arrange
        var cut = Render("Profile", "/Account/Manage/Profile");

        // Act
        cut.Find("button").Click();

        // Assert
        Assert.Equal("http://localhost/Account/Manage/Profile", Nav.Uri);
    }

    #endregion
}
