using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the full registration → MailHog → email-confirmation → login loop through this app's
/// sample host and its Email Settings wiring. Form-validation and pre-confirmation-login-blocked
/// coverage are marten-identity's own authoritative E2E coverage, not re-tested here (#150).
/// </summary>
public sealed class RegistrationTests(AppFoundationE2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Register_ThenConfirmEmail_AllowsLogin()
    {
        // Arrange — the setup gate must be past so /Account/Register is reachable.
        await Fixture.ProvisionAdminAsync();
        await Fixture.ClearMailAsync(TestContext.Current.CancellationToken);

        // Act
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);
        await LoginAsync(email, TestData.DefaultPassword);

        // Assert — landed somewhere authenticated, not back on the login page.
        Assert.NotEqual("/account/login", new Uri(Page.Url).AbsolutePath.ToLowerInvariant());
    }
}
