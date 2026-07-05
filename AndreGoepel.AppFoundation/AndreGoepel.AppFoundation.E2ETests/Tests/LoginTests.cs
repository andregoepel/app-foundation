using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Covers login success/failure, account lockout, and logout.</summary>
public sealed class LoginTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Login_WithWrongPassword_ShowsInvalidAndStaysOnPage()
    {
        // Arrange
        await LoginAsAdminAsync(); // ensures the admin exists
        await LogoutAsync();
        await Page.GotoAsync("/Account/Login");
        await Page.WaitForBlazorAsync();

        // Act
        await Page.FillFieldAsync("Email", TestData.AdminEmail);
        await Page.FillFieldAsync("Password", "totally-wrong-1!");
        await Page.ClickButtonAsync("Log in");

        // Assert
        await Expect(Page.GetByText("Invalid login attempt")).ToBeVisibleAsync();
        Assert.Equal("/Account/Login", new Uri(Page.Url).AbsolutePath);
    }

    [Fact]
    public async Task Login_AfterRepeatedFailures_LocksAccount()
    {
        // Arrange — a confirmed user (lockout only applies once sign-in is otherwise allowed).
        await Fixture.ProvisionAdminAsync();
        await Fixture.MailHog.ClearAsync();
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);

        // Act — hammer wrong passwords until the middleware redirects to the lockout page.
        var lockedOut = false;
        for (var attempt = 0; attempt < 7 && !lockedOut; attempt++)
        {
            await Page.GotoAsync("/Account/Login");
            await Page.WaitForBlazorAsync();
            await Page.FillFieldAsync("Email", email);
            await Page.FillFieldAsync("Password", "wrong-password-1!");
            await Page.ClickButtonAsync("Log in");
            await Page.WaitForTimeoutAsync(300);
            lockedOut = Page.Url.Contains("/Account/Lockout", StringComparison.OrdinalIgnoreCase);
        }

        // Assert
        Assert.True(lockedOut, "Account should have been locked out after repeated failures.");
    }

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
