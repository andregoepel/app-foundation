using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the Administrator-only management area's data wiring and its authorization boundary
/// through this app's sample host. Role CRUD is marten-identity's own authoritative E2E coverage,
/// not re-tested here (#150).
/// </summary>
public sealed class AdministrationTests(AppFoundationE2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Admin_CanViewUsers_ListingIncludesAdminAccount()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/Administration/Users");
        await Page.WaitForBlazorAsync();

        // Assert — the admin's own account is listed in the grid. Scoped to the grid
        // because the topbar user chip shows the same email.
        await Expect(Page.Locator(".rz-data-grid").GetByText(TestData.AdminEmail))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task NonAdmin_AccessingAdministration_IsBouncedAway()
    {
        // Arrange — a confirmed non-admin user.
        await Fixture.ProvisionAdminAsync();
        await Fixture.ClearMailAsync(TestContext.Current.CancellationToken);
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);
        await LoginAsync(email, TestData.DefaultPassword);

        // Act
        await Page.GotoAsync("/Administration/Roles");

        // Assert — the Administrator-only page never renders for a non-admin: the
        // authorization redirect bounces them off the /Administration path (to the app
        // home, since they are already authenticated — an anonymous visitor would instead
        // be sent to the login page).
        await Page.WaitForURLAsync(url =>
            !new Uri(url).AbsolutePath.Contains(
                "Administration",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }
}
