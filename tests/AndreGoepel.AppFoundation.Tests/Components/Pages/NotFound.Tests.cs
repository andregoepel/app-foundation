using AndreGoepel.AppFoundation.Components.Pages;
using Bunit;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

public class NotFoundTests : BunitContext
{
    [Fact]
    public void Render_ShowsNotFoundMessage()
    {
        // Arrange
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<NotFound>();

        // Assert
        Assert.Contains("Page not found", cut.Markup);
    }
}
