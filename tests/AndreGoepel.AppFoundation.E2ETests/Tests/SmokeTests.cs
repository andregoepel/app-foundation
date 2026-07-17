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
    public async Task Setup_CreatesDefaultRoles_MemberAndUser()
    {
        // Arrange — Setup creates the default roles while nobody is signed in yet, so the
        // writes only land inside the authorizer's system scope. Without it the role store
        // fails closed and the roles silently never appear (#89).
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/Administration/Roles");

        // Assert — scoped to the grid: "Users" also appears as a nav button on this page.
        var grid = Page.Locator(".rz-data-grid");
        await Expect(grid.GetByText("Member", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(grid.GetByText("User", new() { Exact = true })).ToBeVisibleAsync();
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
}
