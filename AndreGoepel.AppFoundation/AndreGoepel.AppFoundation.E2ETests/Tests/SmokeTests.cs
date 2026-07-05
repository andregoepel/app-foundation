using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Fast confidence checks that the harness boots the app and the core happy path works.</summary>
public sealed class SmokeTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Setup_ProvisionsAdmin_AndSetupPageIsShownOnlyOnce()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/Setup");
        await Page.WaitForBlazorAsync();

        // Assert — once an admin exists, Setup redirects away from itself.
        Assert.DoesNotContain(
            "/Setup",
            new Uri(Page.Url).AbsolutePath,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task Admin_CanLogIn_AndReachDashboard()
    {
        // Arrange / Act
        await LoginAsAdminAsync();
        await Page.GotoAsync("/dashboard");
        await Page.WaitForBlazorAsync();

        // Assert
        await Page.AssertOnPathAsync("dashboard");
    }

    [Fact]
    public async Task PublicLandingPage_Renders()
    {
        // Arrange — a user must exist so the setup gate lets public pages through.
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert
        Assert.Contains("André", await Page.TitleAsync());
    }
}
