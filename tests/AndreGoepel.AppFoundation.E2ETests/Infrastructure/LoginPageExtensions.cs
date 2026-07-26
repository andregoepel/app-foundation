namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>
/// App-specific IPage login helper, needed by
/// <see cref="AppFoundationE2EAppFixture.EnsureEmailConfiguredAsync"/> to log a page in during
/// fixture setup — before any <see cref="E2ETestBase"/> instance (and the shared package's
/// <c>E2ETestBase{TFixture}.LoginAsync</c> it inherits) exists. Mirrors that package method's
/// login-retry logic; kept local because nothing outside fixture setup needs it — every test
/// instead reaches login through <c>E2ETestBase{TFixture}.LoginAsync</c>/<c>LoginAsAdminAsync</c>.
/// </summary>
internal static class LoginPageExtensions
{
    public static async Task LoginAsync(this IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.WaitForBlazorAsync();
        await page.FillFieldAsync("Email", email);
        await page.FillFieldAsync("Password", password);
        await page.ClickAndLeaveLoginAsync();
    }

    /// <summary>
    /// Clicks "Log in" and waits to leave the login page. A click can land in the gap between the
    /// circuit connecting and the form's submit handler attaching — it is then silently lost — so
    /// the click is retried until the cookie middleware redirects away.
    /// </summary>
    private static async Task ClickAndLeaveLoginAsync(this IPage page)
    {
        for (var attempt = 0; ; attempt++)
        {
            await page.ClickButtonAsync("Log in");
            try
            {
                await page.WaitForURLAsync(
                    url =>
                        !new Uri(url).AbsolutePath.Equals(
                            "/Account/Login",
                            StringComparison.OrdinalIgnoreCase
                        ),
                    new PageWaitForURLOptions { Timeout = 5_000 }
                );
                return;
            }
            catch (TimeoutException) when (attempt < 5)
            {
                // Submit was lost before the handler attached — click again.
            }
        }
    }
}
