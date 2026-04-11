using AndreGoepel.MembersArea.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.MembersArea.Tests.Shared;

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
        var cut = Render("Go somewhere", "/somewhere");

        Assert.Contains("Go somewhere", cut.Markup);
    }

    #endregion

    #region Navigation

    [Fact]
    public void Click_NavigatesToPath()
    {
        var cut = Render("Click me", "/target");

        cut.Find("button").Click();

        Assert.Equal("http://localhost/target", Nav.Uri);
    }

    [Fact]
    public void Click_NavigatesToCorrectPathWhenMultipleSegments()
    {
        var cut = Render("Profile", "/Account/Manage/Profile");

        cut.Find("button").Click();

        Assert.Equal("http://localhost/Account/Manage/Profile", Nav.Uri);
    }

    #endregion
}
