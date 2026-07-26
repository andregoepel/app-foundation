using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the pages that live in this repo, not in the shared
/// <c>AndreGoepel.Marten.Identity.Blazor</c> package: the sample host's own home page and
/// AppFoundation's Email Settings admin screen. Login Features — a shared identity-blazor admin
/// page rather than app-foundation's own — is marten-identity's own authoritative E2E coverage,
/// not re-tested here (#150).
/// </summary>
public sealed class AppPagesTests(AppFoundationE2EAppFixture fixture) : E2ETestBase(fixture)
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
}
