using System.Globalization;
using AndreGoepel.AppFoundation.Components.Layout;
using Bunit;

namespace AndreGoepel.AppFoundation.Tests.Components.Layout;

public sealed class ReconnectModalLocalizationTests : BunitContext
{
    [Fact]
    public void Render_English_ShowsEnglishCopy()
    {
        // Arrange / Act
        var cut = Render<ReconnectModal>();

        // Assert
        Assert.Contains("Rejoining the server...", cut.Markup);
        Assert.Contains("Retry", cut.Markup);
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
            var cut = Render<ReconnectModal>();

            // Assert
            Assert.Contains("Verbindung zum Server wird wiederhergestellt", cut.Markup);
            Assert.Contains("Erneut versuchen", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
