using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Covers the host/application pages: the sample home and the AppFoundation admin screens.</summary>
public sealed class AppPagesTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task SampleHome_Renders()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert
        Assert.Contains("AppFoundation Sample", await Page.TitleAsync());
    }

    [Fact]
    public async Task EmailSettings_LoadsForAdministrator()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/Administration/EmailSettings");
        await Page.WaitForBlazorAsync();

        // Assert
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Email Settings" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task LoginFeatures_LoadsForAdministrator()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/Administration/LoginFeatures");
        await Page.WaitForBlazorAsync();

        // Assert
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Login Features" }))
            .ToBeVisibleAsync();
    }
}
