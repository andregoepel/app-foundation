using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Covers the self-service registration path and its email-confirmation gate.</summary>
public sealed class RegistrationTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Register_ThenConfirmEmail_AllowsLogin()
    {
        // Arrange — the setup gate must be past so /Account/Register is reachable.
        await Fixture.ProvisionAdminAsync();
        await Fixture.MailHog.ClearAsync();

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

    [Fact]
    public async Task Register_BeforeConfirmingEmail_CannotLogIn()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();
        var email = await RegisterAsync();
        await Page.WaitForURLAsync(url =>
            url.Contains("RegisterConfirmation", StringComparison.OrdinalIgnoreCase)
        );

        // Act — attempt login without confirming.
        await Page.GotoAsync("/Account/Login");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("Email", email);
        await Page.FillFieldAsync("Password", TestData.DefaultPassword);
        await Page.ClickButtonAsync("Log in");

        // Assert — still on login and told it was invalid; account not usable yet.
        await Expect(Page.GetByText("Invalid login attempt")).ToBeVisibleAsync();
        Assert.Equal("/Account/Login", new Uri(Page.Url).AbsolutePath);
    }

    [Fact]
    public async Task Register_WithMismatchedPasswords_ShowsValidationError()
    {
        // Arrange
        await Fixture.ProvisionAdminAsync();
        await Page.GotoAsync("/Account/Register");
        await Page.WaitForBlazorAsync();

        // Act
        await Page.FillFieldAsync("Email", TestData.NewEmail());
        await Page.FillFieldAsync("NewPassword", TestData.DefaultPassword);
        await Page.FillFieldAsync("ConfirmPassword", "different-Pass1!");
        await Page.ClickButtonAsync("Register");

        // Assert
        await Expect(Page.GetByText("The passwords do not match")).ToBeVisibleAsync();
    }
}
