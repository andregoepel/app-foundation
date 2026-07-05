using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Covers the public website, the content admin page, and framework navigation (not-found).</summary>
public sealed class WebsiteAndNavigationTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Landing_RendersPublicHomePage()
    {
        // Arrange — a user must exist so the setup gate lets the public site through.
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/");
        await Page.WaitForBlazorAsync();

        // Assert
        Assert.Contains("André", await Page.TitleAsync());
    }

    [Fact]
    public async Task ContentAdmin_LoadsForAdministrator()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/admin/content");
        await Page.WaitForBlazorAsync();

        // Assert — the content editor heading is shown (loads once content is fetched).
        await Expect(Page.GetByText("Texte")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UnknownRoute_ShowsNotFoundPage()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/this-route-truly-does-not-exist-" + Guid.NewGuid().ToString("N"));
        await Page.WaitForBlazorAsync();

        // Assert
        await Expect(Page.GetByText("Page not found")).ToBeVisibleAsync();
    }
}
