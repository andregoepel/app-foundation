using System.Globalization;
using AndreGoepel.AppFoundation.Components.Pages;
using Bunit;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

public sealed class ErrorLocalizationTests : BunitContext
{
    [Fact]
    public void Render_English_ShowsEnglishCopy()
    {
        // Arrange / Act
        var cut = Render<Error>();

        // Assert
        Assert.Contains("An error occurred while processing your request.", cut.Markup);
        Assert.Contains("Development mode", cut.Markup);
        Assert.Contains("<strong>Development</strong>", cut.Markup);
    }

    [Fact]
    public void Render_German_ShowsGermanCopy()
    {
        // Arrange
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        try
        {
            // Act
            var cut = Render<Error>();

            // Assert
            Assert.Contains(
                "Bei der Verarbeitung Ihrer Anfrage ist ein Fehler aufgetreten.",
                cut.Markup
            );
            Assert.Contains("Entwicklungsmodus", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
