using System.Resources;
using AndreGoepel.AppFoundation.Resources;
using AndreGoepel.Design.Blazor.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

/// <summary>
/// Setup.razor's InputModel can't be rendered directly from outside the assembly, and mocking
/// its full DI graph (IQuerySession, UserManager, RoleManager, SignInManager, ...) just to prove
/// its markup resolves through T(...) is disproportionate — see SetupValidatorConversionTests for
/// the same reasoning applied to its validation. This instead pins down the fallback resolution
/// path AppFoundationLocalizedComponentBase.T(...) itself calls
/// (IServiceProvider.LocalizedText&lt;AppFoundationStrings&gt;), the same mechanism
/// ErrorLocalizationTests/EmailSettingsPageLocalizationTests exercise indirectly via a full
/// render.
/// </summary>
public sealed class SetupLocalizationTests
{
    private static readonly IServiceProvider Services =
        new ServiceCollection().BuildServiceProvider();

    private static readonly ResourceManager Fallback = new(
        typeof(AppFoundationStrings).FullName!,
        typeof(AppFoundationStrings).Assembly
    );

    [Fact]
    public void English_ResolvesEnglishCopy()
    {
        // Arrange
        using var culture = CultureScope.UiOnly("en");

        // Act / Assert
        Assert.Equal(
            "Initial setup",
            Services.LocalizedText<AppFoundationStrings>("Setup.PageTitle", Fallback)
        );
        Assert.Equal(
            "Create admin & complete setup",
            Services.LocalizedText<AppFoundationStrings>("Setup.SubmitButton", Fallback)
        );
        Assert.Equal(
            "The passwords do not match",
            Services.LocalizedText<AppFoundationStrings>("Setup.PasswordsDoNotMatch", Fallback)
        );
        Assert.Equal(
            "Error creating role Member",
            Services.LocalizedText<AppFoundationStrings>(
                "Setup.ErrorCreatingRoleTitle",
                Fallback,
                "Member"
            )
        );
    }

    [Fact]
    public void German_ResolvesGermanCopy()
    {
        // Arrange
        using var culture = CultureScope.UiOnly("de");

        // Act / Assert
        Assert.Equal(
            "Ersteinrichtung",
            Services.LocalizedText<AppFoundationStrings>("Setup.PageTitle", Fallback)
        );
        Assert.Equal(
            "Administrator anlegen & Einrichtung abschließen",
            Services.LocalizedText<AppFoundationStrings>("Setup.SubmitButton", Fallback)
        );
        Assert.Equal(
            "Die Passwörter stimmen nicht überein",
            Services.LocalizedText<AppFoundationStrings>("Setup.PasswordsDoNotMatch", Fallback)
        );
        Assert.Equal(
            "Fehler beim Anlegen der Rolle Member",
            Services.LocalizedText<AppFoundationStrings>(
                "Setup.ErrorCreatingRoleTitle",
                Fallback,
                "Member"
            )
        );
    }
}
