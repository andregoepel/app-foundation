using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the full self-service change-password loop — change it, then log in with the new one —
/// through this app's sample host. Profile updates and account deletion are marten-identity's own
/// authoritative E2E coverage, not re-tested here (#150).
/// </summary>
public sealed class AccountManagementTests(AppFoundationE2EAppFixture fixture)
    : E2ETestBase(fixture)
{
    [Fact]
    public async Task ChangePassword_ThenLoginWithNewPassword_Succeeds()
    {
        // Arrange
        var email = await CreateConfirmedUserAndLoginAsync();
        await Page.GotoAsync("/Account/Manage/ChangePassword");
        await Page.WaitForBlazorAsync();

        // Act
        await Page.FillFieldAsync("OldPassword", TestData.DefaultPassword);
        await Page.FillFieldAsync("NewPassword", TestData.AlternatePassword);
        await Page.FillFieldAsync("ConfirmPassword", TestData.AlternatePassword);
        await Page.ClickButtonAsync("Update Password");
        await Expect(Page.GetByText("password has been updated")).ToBeVisibleAsync();

        // Assert — the new password is the one that now works.
        await LogoutAsync();
        await LoginAsync(email, TestData.AlternatePassword);
        Assert.NotEqual("/account/login", new Uri(Page.Url).AbsolutePath.ToLowerInvariant());
    }

    #region Helpers

    private async Task<string> CreateConfirmedUserAndLoginAsync()
    {
        await Fixture.ProvisionAdminAsync();
        await Fixture.ClearMailAsync();
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);
        await LoginAsync(email, TestData.DefaultPassword);
        return email;
    }

    #endregion
}
