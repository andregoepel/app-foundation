using System.Globalization;
using AndreGoepel.AppFoundation.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

/// <summary>
/// Setup.razor's InputModel can't be rendered directly from outside the assembly, and mocking
/// its full DI graph (IQuerySession, UserManager, RoleManager, SignInManager, ...) just to prove
/// its markup resolves through T(...) is disproportionate — see SetupValidatorConversionTests for
/// the same reasoning applied to its validation. This instead pins down the fallback resolution
/// path LocalizedComponentBase.T(...) itself calls (IServiceProvider.AppFoundationText), the same
/// mechanism ErrorLocalizationTests/EmailSettingsPageLocalizationTests exercise indirectly via a
/// full render.
/// </summary>
public sealed class SetupLocalizationTests
{
    private static readonly IServiceProvider Services =
        new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void English_ResolvesEnglishCopy()
    {
        // Arrange
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            // Act / Assert
            Assert.Equal("Initial setup", Services.AppFoundationText("Setup.PageTitle"));
            Assert.Equal(
                "Create admin & complete setup",
                Services.AppFoundationText("Setup.SubmitButton")
            );
            Assert.Equal(
                "The passwords do not match",
                Services.AppFoundationText("Setup.PasswordsDoNotMatch")
            );
            Assert.Equal(
                "Error creating role Member",
                Services.AppFoundationText("Setup.ErrorCreatingRoleTitle", "Member")
            );
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void German_ResolvesGermanCopy()
    {
        // Arrange
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        try
        {
            // Act / Assert
            Assert.Equal("Ersteinrichtung", Services.AppFoundationText("Setup.PageTitle"));
            Assert.Equal(
                "Administrator anlegen & Einrichtung abschließen",
                Services.AppFoundationText("Setup.SubmitButton")
            );
            Assert.Equal(
                "Die Passwörter stimmen nicht überein",
                Services.AppFoundationText("Setup.PasswordsDoNotMatch")
            );
            Assert.Equal(
                "Fehler beim Anlegen der Rolle Member",
                Services.AppFoundationText("Setup.ErrorCreatingRoleTitle", "Member")
            );
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
