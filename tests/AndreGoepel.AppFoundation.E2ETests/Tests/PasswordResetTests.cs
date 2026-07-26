using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the full forgot-password → emailed link → reset → login loop through this app's sample
/// host and its Email Settings wiring. Invalid-link and resend-confirmation messaging are
/// marten-identity's own authoritative E2E coverage, not re-tested here (#150).
/// </summary>
public sealed class PasswordResetTests(AppFoundationE2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task ForgotPassword_ResetLink_AllowsLoginWithNewPassword()
    {
        // Arrange — a confirmed user we can safely change (never the shared admin).
        await Fixture.ProvisionAdminAsync();
        await Fixture.ClearMailAsync(TestContext.Current.CancellationToken);
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);

        // Act — request the reset, follow the emailed link, set a new password.
        await Fixture.ClearMailAsync(TestContext.Current.CancellationToken);
        await Page.GotoAsync("/Account/ForgotPassword");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("Email", email);
        await Page.ClickButtonAsync("Reset password");
        await Page.AssertOnPathAsync("Account/ForgotPasswordConfirmation");

        var resetLink = await Fixture.Mail!.WaitForLinkAsync(
            email,
            "Account/ResetPassword",
            ct: TestContext.Current.CancellationToken
        );
        await Page.GotoAsync(resetLink);
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("Email", email);
        await Page.FillFieldAsync("Password", TestData.AlternatePassword);
        await Page.FillFieldAsync("ConfirmPassword", TestData.AlternatePassword);
        await Page.ClickButtonAsync("Reset password");
        await Page.AssertOnPathAsync("Account/ResetPasswordConfirmation");

        // Assert — the new password works, the old one is irrelevant.
        await LoginAsync(email, TestData.AlternatePassword);
        Assert.NotEqual("/account/login", new Uri(Page.Url).AbsolutePath.ToLowerInvariant());
    }
}
