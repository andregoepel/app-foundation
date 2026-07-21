using System.Globalization;
using AndreGoepel.AppFoundation.Components.Pages;
using Bunit;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

public class HomeLocalizationTests : BunitContext
{
    [Fact]
    public void Render_English_ShowsEnglishCopy()
    {
        var cut = Render<Home>();

        Assert.Contains("Dashboard", cut.Markup);
        Assert.Contains("Welcome!", cut.Markup);
    }

    [Fact]
    public void Render_German_ShowsGermanCopy()
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        try
        {
            var cut = Render<Home>();

            Assert.Contains("Willkommen!", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
