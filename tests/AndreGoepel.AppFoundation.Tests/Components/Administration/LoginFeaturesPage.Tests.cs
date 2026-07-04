using AndreGoepel.AppFoundation.Components.Administration.Pages;
using AndreGoepel.AppFoundation.Identity;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.AppFoundation.Tests.Components.Administration;

public class LoginFeaturesPageTests : BunitContext
{
    private readonly IIdentityFeatureSettingsStore store =
        Substitute.For<IIdentityFeatureSettingsStore>();

    public LoginFeaturesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(store);
        Services.AddSingleton(new NotificationService());
    }

    private static IdentityFeatureSettings Settings(
        bool fromConfiguration = false,
        bool passkey = true
    ) =>
        new()
        {
            EnableUserRegistration = true,
            EnableTwoFactor = true,
            EnablePasskey = passkey,
            FromConfiguration = fromConfiguration,
        };

    [Fact]
    public void Render_FromConfiguration_ShowsBaselineHint()
    {
        // Arrange
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Settings(fromConfiguration: true));

        // Act
        var cut = Render<LoginFeaturesPage>();

        // Assert
        Assert.Contains("configuration baseline", cut.Markup);
    }

    [Fact]
    public void Render_FromDatabase_HidesBaselineHint()
    {
        // Arrange
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Settings());

        // Act
        var cut = Render<LoginFeaturesPage>();

        // Assert
        Assert.DoesNotContain("configuration baseline", cut.Markup);
    }

    [Fact]
    public void Submit_PersistsTheLoadedFlags()
    {
        // Arrange
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Settings(passkey: false));
        var cut = Render<LoginFeaturesPage>();

        // Act
        cut.Find("form").Submit();

        // Assert
        cut.WaitForAssertion(() =>
            store
                .Received(1)
                .SaveAsync(
                    Arg.Is<IdentityFeatureSettings>(s =>
                        s.EnableUserRegistration && s.EnableTwoFactor && !s.EnablePasskey
                    ),
                    Arg.Any<CancellationToken>()
                )
        );
    }

    [Fact]
    public void Route_IsAdministrationLoginFeatures_AndRequiresAdministratorRole()
    {
        // Act
        var route = Attribute.GetCustomAttribute(typeof(LoginFeaturesPage), typeof(RouteAttribute));
        var authorize =
            Attribute.GetCustomAttribute(typeof(LoginFeaturesPage), typeof(AuthorizeAttribute))
            as AuthorizeAttribute;

        // Assert
        Assert.Equal(
            "/Administration/LoginFeatures",
            Assert.IsType<RouteAttribute>(route).Template
        );
        Assert.NotNull(authorize);
        Assert.Equal("Administrator", authorize.Roles);
    }
}
