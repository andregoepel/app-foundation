using AndreGoepel.AppFoundation.E2ETests.Infrastructure;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>Covers the self-service account management pages under /Account/Manage.</summary>
public sealed class AccountManagementTests(E2EAppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Profile_UpdatePhoneNumber_ShowsSuccess()
    {
        // Arrange
        await CreateConfirmedUserAndLoginAsync();
        await Page.GotoAsync("/Account/Manage/Profile");
        await Page.WaitForBlazorAsync();

        // Act
        await Page.FillFieldAsync("PhoneNumber", "+49 123 4567890");
        await Page.ClickButtonAsync("Save Changes");

        // Assert
        await Expect(Page.GetByText("Your profile has been updated")).ToBeVisibleAsync();
    }

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

    [Fact]
    public async Task DeleteAccount_WithPassword_RemovesAccount()
    {
        // Arrange
        var email = await CreateConfirmedUserAndLoginAsync();
        await Page.GotoAsync("/Account/Manage/DeletePersonalData");
        await Page.WaitForBlazorAsync();

        // Act — submit triggers a Radzen confirm dialog that must be accepted.
        await Page.FillFieldAsync("Password", TestData.DefaultPassword);
        await Page.ClickButtonAsync("Permanently Delete My Account");
        await Page.ClickButtonAsync("Yes, Delete My Account");

        // Assert — the deleted account can no longer log in.
        await Page.GotoAsync("/Account/Login");
        await Page.WaitForBlazorAsync();
        await Page.FillFieldAsync("Email", email);
        await Page.FillFieldAsync("Password", TestData.DefaultPassword);
        await Page.ClickButtonAsync("Log in");
        await Expect(Page.GetByText("Invalid login attempt")).ToBeVisibleAsync();
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

    #endregion
}
