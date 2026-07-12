using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Covers the forgot-password / reset-password / resend-confirmation flows end to end via MailHog.</summary>
public sealed class PasswordResetTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task ForgotPassword_ResetLink_AllowsLoginWithNewPassword()
    {
        // Arrange — a confirmed user we can safely change (never the shared admin).
        await Fixture.ProvisionAdminAsync();
        await Fixture.MailHog.ClearAsync();
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);

        // Act — request the reset, follow the emailed link, set a new password.
        await Fixture.MailHog.ClearAsync();
        await Page.GotoAsync("/Account/ForgotPassword");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("Email", email);
        await Page.ClickButtonAsync("Reset password");
        await Page.AssertOnPathAsync("Account/ForgotPasswordConfirmation");

        var resetLink = await Fixture.MailHog.WaitForLinkAsync(email, "Account/ResetPassword");
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

    [Fact]
    public async Task ResetPassword_WithoutCode_ShowsInvalidLink()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/Account/ResetPassword");
        await Page.WaitForBlazorAsync();

        // Assert
        await Expect(Page.GetByText("password reset link is invalid")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ResendEmailConfirmation_ShowsSentMessage()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();

        // Act
        await Page.GotoAsync("/Account/ResendEmailConfirmation");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("Email", TestData.NewEmail());
        await Page.ClickButtonAsync("Resend");

        // Assert — the app never reveals whether the address exists.
        await Expect(Page.GetByText("Verification email sent")).ToBeVisibleAsync();
    }
}
