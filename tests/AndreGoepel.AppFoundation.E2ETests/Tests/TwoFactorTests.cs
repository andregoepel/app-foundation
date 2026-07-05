using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the TOTP two-factor lifecycle: enabling via authenticator, logging in with a generated code,
/// logging in with a recovery code, and disabling. Each test uses its own user so enabling 2FA never
/// affects the shared admin account.
/// </summary>
public sealed class TwoFactorTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Enable2fa_ThenLogin_RequiresAuthenticatorCode()
    {
        // Arrange
        var (email, sharedKey, _) = await CreateUserWithTwoFactorAsync();

        // Act — a fresh login must be challenged for a code, which we compute from the shared key.
        await LogoutAsync();
        await LoginAsync(email, TestData.DefaultPassword);
        await Page.AssertOnPathAsync("Account/LoginWith2fa");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("TwoFactorCode", Totp.Compute(sharedKey));
        await Page.ClickButtonAsync("Log in");

        // Assert — challenge cleared, no longer on any login page.
        await Page.WaitForURLAsync(url =>
            !new Uri(url).AbsolutePath.StartsWith(
                "/Account/Login",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    [Fact]
    public async Task Enable2fa_ThenLoginWithRecoveryCode_Succeeds()
    {
        // Arrange
        var (email, _, recoveryCodes) = await CreateUserWithTwoFactorAsync();
        Assert.NotEmpty(recoveryCodes);

        // Act
        await LogoutAsync();
        await LoginAsync(email, TestData.DefaultPassword);
        await Page.AssertOnPathAsync("Account/LoginWith2fa");
        await Page.WaitForBlazorAsync();
        await Page.ClickLinkAsync("Use a recovery code instead");
        await Page.AssertOnPathAsync("Account/LoginWithRecoveryCode");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("RecoveryCode", recoveryCodes[0]);
        await Page.ClickButtonAsync("Log in");

        // Assert
        await Page.WaitForURLAsync(url =>
            !new Uri(url).AbsolutePath.StartsWith(
                "/Account/Login",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    [Fact]
    public async Task Disable2fa_RemovesTheChallenge()
    {
        // Arrange
        var (email, _, _) = await CreateUserWithTwoFactorAsync();

        // Act — disable, then log in fresh.
        await Page.GotoAsync("/Account/Manage/Disable2fa");
        await Page.WaitForBlazorAsync();
        await Page.ClickButtonAsync("Disable 2FA");
        await Page.AssertOnPathAsync("Account/Manage/TwoFactorAuthentication");
        await LogoutAsync();
        await LoginAsync(email, TestData.DefaultPassword);

        // Assert — login completes without a 2FA challenge.
        Assert.DoesNotContain("LoginWith2fa", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    #region Helpers

    /// <summary>Registers &amp; confirms a user, logs them in, enables TOTP 2FA, and returns its secrets.</summary>
    private async Task<(
        string Email,
        string SharedKey,
        IReadOnlyList<string> RecoveryCodes
    )> CreateUserWithTwoFactorAsync()
    {
        await Fixture.ProvisionAdminAsync();
        await Fixture.MailHog.ClearAsync();
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);
        await LoginAsync(email, TestData.DefaultPassword);

        await Page.GotoAsync("/Account/Manage/EnableAuthenticator");
        await Page.WaitForBlazorAsync();

        var sharedKey = (await Page.Locator("strong").First.InnerTextAsync()).Trim();
        await Page.FillFieldAsync("VerificationCode", Totp.Compute(sharedKey));
        await Page.ClickButtonAsync("Verify");

        await Expect(Page.GetByText("Put these codes in a safe place")).ToBeVisibleAsync();
        var recoveryCodes = await Page.Locator("[style*='font-family: monospace']")
            .AllInnerTextsAsync();

        return (
            email,
            sharedKey,
            recoveryCodes.Select(c => c.Trim()).Where(c => c.Length > 0).ToList()
        );
    }

    #endregion
}
