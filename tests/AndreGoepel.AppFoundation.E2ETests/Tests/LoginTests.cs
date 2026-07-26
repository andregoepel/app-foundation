using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers login/logout wiring through this app's sample host. Login success is already proven by
/// <c>SmokeTests.Admin_CanLogIn_AndReachDashboard</c>; wrong-password messaging, lockout, and
/// other identity-library behavior are marten-identity's own authoritative E2E coverage, not
/// re-tested here (#150).
/// </summary>
public sealed class LoginTests(AppFoundationE2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Logout_ThenAccessingProtectedPage_RedirectsToLogin()
    {
        // Arrange
        await LoginAsAdminAsync();
        await Page.GotoAsync("/Account/Manage/Profile");
        await Page.WaitForBlazorAsync();

        // Act
        await LogoutAsync();
        await Page.GotoAsync("/Account/Manage/Profile");
        await Page.WaitForBlazorAsync();

        // Assert — the protected page bounces an anonymous visitor to login.
        await Page.AssertOnPathAsync("Account/Login");
    }
}
