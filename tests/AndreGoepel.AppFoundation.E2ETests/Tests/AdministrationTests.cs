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

        // Assert — denial returns the authenticated user to the home page.
        await Expect(Page).ToHaveURLAsync(new Uri(new Uri(Fixture.AppBaseUrl), "/").AbsoluteUri);
        await Expect(Page.Locator(".ag-user-email")).ToHaveTextAsync(email);
        await Expect(Page.Locator(".rz-data-grid")).ToHaveCountAsync(0);
    }
}
