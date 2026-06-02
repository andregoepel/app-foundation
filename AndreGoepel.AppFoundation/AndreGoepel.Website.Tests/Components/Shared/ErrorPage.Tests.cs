using AndreGoepel.Website.Components.Shared;
using AndreGoepel.Website.Tests.Components;
using Bunit;

namespace AndreGoepel.Website.Tests.Components.Shared;

public class ErrorPageTests : ErrorComponentContext
{
    private IRenderedComponent<ErrorPage> RenderError(string code) =>
        Render<ErrorPage>(p => p.Add(c => c.Code, code));

    [Fact]
    public void Render_With404_ShowsNotFoundCopy()
    {
        // Act
        var cut = RenderError("404");

        // Assert
        Assert.Contains("Error 404 · Not Found", cut.Find(".err-eyebrow").TextContent);
        Assert.Equal("This page took a wrong turn.", cut.Find(".err-title").TextContent);
        Assert.Contains("doesn't exist", cut.Find(".err-body").TextContent);
        Assert.Equal("/page-not-found", cut.Find(".err-terminal-path").TextContent);
        Assert.Equal("404", cut.Find(".err-terminal-status").TextContent);
    }

    [Fact]
    public void Render_With403_ShowsForbiddenCopy()
    {
        // Act
        var cut = RenderError("403");

        // Assert
        Assert.Contains("Error 403 · Forbidden", cut.Find(".err-eyebrow").TextContent);
        Assert.Equal("This area is off-limits.", cut.Find(".err-title").TextContent);
        Assert.Equal("/restricted", cut.Find(".err-terminal-path").TextContent);
        Assert.Equal("403", cut.Find(".err-terminal-status").TextContent);
    }

    [Fact]
    public void Render_PrimaryActionLinksToHomepage()
    {
        // Act
        var cut = RenderError("404");

        // Assert
        Assert.Equal("/", cut.Find("a.btn-primary").GetAttribute("href"));
    }
}
