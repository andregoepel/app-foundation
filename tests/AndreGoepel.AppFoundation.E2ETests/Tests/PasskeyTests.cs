using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Covers the WebAuthn/passkey journeys using a Chromium CDP virtual authenticator that
/// auto-satisfies user presence/verification, so create/get ceremonies complete headlessly.
/// </summary>
public sealed class PasskeyTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task RegisterPasskey_ThenRename_AppearsInList()
    {
        // Arrange
        await CreateConfirmedUserAndLoginAsync();
        await VirtualAuthenticator.EnableAsync(Context, Page);

        // Act
        await RegisterPasskeyAsync("My Test Key");

        // Assert — the named passkey shows up in the management grid.
        await Page.AssertOnPathAsync("Account/Manage/Passkeys");
        await Expect(Page.GetByText("My Test Key")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RegisterPasskey_ThenLoginWithPasskey_Succeeds()
    {
        // Arrange — create a passkey, then sign out (credential stays in the virtual authenticator).
        await CreateConfirmedUserAndLoginAsync();
        await VirtualAuthenticator.EnableAsync(Context, Page);
        await RegisterPasskeyAsync("Login Key");
        await LogoutAsync();

        // Act — sign in with the passkey. On the login page the browser's conditional
        // mediation often picks up the resident credential from the virtual authenticator
        // and completes the assertion on its own; if it hasn't, click the passkey button
        // to trigger it explicitly. Either way the ceremony ends by leaving the login page.
        await Page.GotoAsync("/Account/Login");
        await Page.WaitForBlazorAsync();
        try
        {
            await Page.GetByText("Log in with a passkey")
                .ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // Conditional mediation already signed in and navigated away — nothing to click.
        }

        // Assert — the assertion ceremony logs us in and leaves the login page.
        await Page.WaitForURLAsync(url =>
            !new Uri(url).AbsolutePath.StartsWith(
                "/Account/Login",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    #region Helpers

    private async Task<string> CreateConfirmedUserAndLoginAsync()
    {
        await Fixture.ProvisionAdminAsync();
        await Fixture.MailHog.ClearAsync();
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );
        await ConfirmEmailAsync(email);
        await LoginAsync(email, TestData.DefaultPassword);
        return email;
    }

    private async Task RegisterPasskeyAsync(string name)
    {
        await Page.GotoAsync("/Account/Manage/Passkeys/Create");
        await Page.WaitForBlazorAsync();
        // Let the PasskeySubmit JS module finish importing before triggering the ceremony.
        await Page.WaitForTimeoutAsync(500);
        await Page.GetByText("Register Passkey").ClickAsync();

        // On success the app routes to the rename page for the new credential.
        await Page.WaitForURLAsync(url =>
            url.Contains("Passkeys/Rename", StringComparison.OrdinalIgnoreCase)
        );
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("NameField", name);
        await Page.ClickButtonAsync("Save Name");
        await Page.WaitForURLAsync(url =>
            new Uri(url).AbsolutePath.EndsWith(
                "/Account/Manage/Passkeys",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    #endregion
}
