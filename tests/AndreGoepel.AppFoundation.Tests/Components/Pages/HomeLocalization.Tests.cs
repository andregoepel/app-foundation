using AndreGoepel.AppFoundation.Components.Pages;
using Bunit;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

public sealed class HomeLocalizationTests : BunitContext
{
    [Fact]
    public void Render_English_ShowsEnglishCopy()
    {
        // Arrange / Act
        var cut = Render<Home>();

        // Assert
        Assert.Contains("Dashboard", cut.Markup);
        Assert.Contains("Welcome!", cut.Markup);
    }

    [Fact]
    public void Render_German_ShowsGermanCopy()
    {
        // Arrange
        using var culture = CultureScope.UiOnly("de");

        // Act
        var cut = Render<Home>();

        // Assert
        Assert.Contains("Willkommen!", cut.Markup);
    }
}
