using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the TOTP two-factor loop — enable via authenticator, then a fresh login is challenged
/// and cleared with a generated code — through this app's sample host. Recovery-code login and
/// disabling 2FA are marten-identity's own authoritative E2E coverage, not re-tested here (#150).
/// </summary>
public sealed class TwoFactorTests(AppFoundationE2EAppFixture fixture) : E2ETestBase(fixture)
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

    #region Helpers

    /// <summary>Registers &amp; confirms a user, logs them in, enables TOTP 2FA, and returns its secrets.</summary>
    private async Task<(
        string Email,
        string SharedKey,
        IReadOnlyList<string> RecoveryCodes
    )> CreateUserWithTwoFactorAsync()
    {
        await Fixture.ProvisionAdminAsync();
        await Fixture.ClearMailAsync();
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
