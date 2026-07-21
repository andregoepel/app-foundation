using System.Globalization;
using AndreGoepel.AppFoundation.Components.Administration.Pages;
using AndreGoepel.AppFoundation.Identity;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.AppFoundation.Tests.Components.Administration;

public class LoginFeaturesPageLocalizationTests : BunitContext
{
    private readonly IIdentityFeatureSettingsStore store =
        Substitute.For<IIdentityFeatureSettingsStore>();

    public LoginFeaturesPageLocalizationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(store);
        Services.AddSingleton(new NotificationService());
        store
            .LoadAsync(Arg.Any<CancellationToken>())
            .Returns(
                new IdentityFeatureSettings
                {
                    EnableUserRegistration = true,
                    EnableTwoFactor = true,
                    EnablePasskey = true,
                }
            );
    }

    [Fact]
    public void Render_German_ShowsGermanCopy()
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        try
        {
            var cut = Render<LoginFeaturesPage>();

            Assert.Contains("Login-Funktionen", cut.Markup);
            Assert.Contains("Selbstregistrierung", cut.Markup);
            Assert.Contains("Änderungen speichern", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
