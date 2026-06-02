using AndreGoepel.Marten.Identity.Http;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AndreGoepel.Marten.Identity.Tests.Http;

public class CookieLoginMiddlewareTests
{
    #region Helpers

    private static readonly LoginTokenProtector _tokens = new(
        DataProtectionProvider.Create("Tests")
    );

    private static SignInManager<User> BuildSignInManager()
    {
        var store = Substitute.For<IUserStore<User>>();
        var userManager = Substitute.For<UserManager<User>>(
            store,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        return Substitute.For<SignInManager<User>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<User>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<User>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<User>>()
        );
    }

    private static DefaultHttpContext BuildContext(string path, string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = QueryString.Create("token", token);
        return context;
    }

    private static CookieLoginMiddleware BuildMiddleware() => new(_ => Task.CompletedTask);

    private static string? RedirectLocation(DefaultHttpContext context) =>
        context.Response.Headers.Location.ToString();

    #endregion

    #region /login path

    [Fact]
    public async Task Login_Success_NoReturnUrl_RedirectsToDashboard()
    {
        // Arrange
        var token = _tokens.Protect(new LoginInfo("alice@example.com", "pw", false));
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/dashboard", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_Success_WithReturnUrl_RedirectsToReturnUrl()
    {
        // Arrange
        var token = _tokens.Protect(
            new LoginInfo("alice@example.com", "pw", false, "/admin/content")
        );
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/admin/content", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_RequiresTwoFactor_ForwardsReturnUrl()
    {
        // Arrange
        var token = _tokens.Protect(
            new LoginInfo("alice@example.com", "pw", false, "/admin/content")
        );
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.TwoFactorRequired));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Contains("returnUrl=%2Fadmin%2Fcontent", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_RequiresTwoFactor_RedirectsToTwoFaPage()
    {
        // Arrange
        var token = _tokens.Protect(new LoginInfo("alice@example.com", "pw", false));
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.TwoFactorRequired));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.StartsWith("/Account/LoginWith2fa", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_RequiresTwoFactor_IncludesRememberMeFlag()
    {
        // Arrange
        var token = _tokens.Protect(new LoginInfo("alice@example.com", "pw", RememberMe: true));
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.TwoFactorRequired));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Contains("rememberMe=true", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_LockedOut_RedirectsToLockout()
    {
        // Arrange
        var token = _tokens.Protect(new LoginInfo("alice@example.com", "pw", false));
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.LockedOut));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/Account/Lockout", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_Failed_RedirectsToLoginPage()
    {
        // Arrange
        var token = _tokens.Protect(new LoginInfo("alice@example.com", "wrong", false));
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.Failed));
        var context = BuildContext("/login", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/Account/Login", RedirectLocation(context));
    }

    #endregion

    #region /login2fa path

    [Fact]
    public async Task Login2fa_Success_RedirectsToReturnUrl()
    {
        // Arrange
        var token = _tokens.Protect(new TwoFactorLoginInfo("123456", false, false, "/dashboard"));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login2fa", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/dashboard", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_Success_NoReturnUrl_RedirectsToDashboard()
    {
        // Arrange
        var token = _tokens.Protect(new TwoFactorLoginInfo("123456", false, false, null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login2fa", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/dashboard", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_LockedOut_RedirectsToLockout()
    {
        // Arrange
        var token = _tokens.Protect(new TwoFactorLoginInfo("123456", false, false, null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.LockedOut));
        var context = BuildContext("/login2fa", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/Account/Lockout", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_Failed_RedirectsWithErrorQuery()
    {
        // Arrange
        var token = _tokens.Protect(new TwoFactorLoginInfo("wrong", false, false, null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Failed));
        var context = BuildContext("/login2fa", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/Account/LoginWith2fa?error=invalid", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_CodeWithSpacesAndDashes_StripsThemBeforeVerification()
    {
        // Arrange
        var token = _tokens.Protect(new TwoFactorLoginInfo("123 456-789", false, false, null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));

        // Act
        await BuildMiddleware().Invoke(BuildContext("/login2fa", token), signInManager, _tokens);

        // Assert
        await signInManager
            .Received(1)
            .TwoFactorAuthenticatorSignInAsync("123456789", Arg.Any<bool>(), Arg.Any<bool>());
    }

    #endregion

    #region /loginrecovery path

    [Fact]
    public async Task LoginRecovery_Success_RedirectsToReturnUrl()
    {
        // Arrange
        var token = _tokens.Protect(new RecoveryCodeLoginInfo("ABCDE-FGHIJ", "/home"));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/loginrecovery", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/home", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_Success_NoReturnUrl_RedirectsToDashboard()
    {
        // Arrange
        var token = _tokens.Protect(new RecoveryCodeLoginInfo("ABCDE-FGHIJ", null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/loginrecovery", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/dashboard", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_LockedOut_RedirectsToLockout()
    {
        // Arrange
        var token = _tokens.Protect(new RecoveryCodeLoginInfo("CODE", null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.LockedOut));
        var context = BuildContext("/loginrecovery", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/Account/Lockout", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_Failed_RedirectsWithErrorQuery()
    {
        // Arrange
        var token = _tokens.Protect(new RecoveryCodeLoginInfo("wrong", null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Failed));
        var context = BuildContext("/loginrecovery", token);

        // Act
        await BuildMiddleware().Invoke(context, signInManager, _tokens);

        // Assert
        Assert.Equal("/Account/LoginWithRecoveryCode?error=invalid", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_CodeWithSpaces_StripsThemBeforeVerification()
    {
        // Arrange
        var token = _tokens.Protect(new RecoveryCodeLoginInfo("ABC DEF GHI", null));
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Success));

        // Act
        await BuildMiddleware()
            .Invoke(BuildContext("/loginrecovery", token), signInManager, _tokens);

        // Assert
        await signInManager.Received(1).TwoFactorRecoveryCodeSignInAsync("ABCDEFGHI");
    }

    #endregion

    #region Unknown / forged tokens

    [Fact]
    public async Task Login_UnknownToken_RedirectsToLogin()
    {
        // Arrange
        var context = BuildContext("/login", "not-a-valid-token");

        // Act
        await BuildMiddleware().Invoke(context, BuildSignInManager(), _tokens);

        // Assert
        Assert.Equal("/Account/Login", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_UnknownToken_RedirectsToLogin()
    {
        // Arrange
        var context = BuildContext("/login2fa", "not-a-valid-token");

        // Act
        await BuildMiddleware().Invoke(context, BuildSignInManager(), _tokens);

        // Assert
        Assert.Equal("/Account/Login", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_UnknownToken_RedirectsToLogin()
    {
        // Arrange
        var context = BuildContext("/loginrecovery", "not-a-valid-token");

        // Act
        await BuildMiddleware().Invoke(context, BuildSignInManager(), _tokens);

        // Assert
        Assert.Equal("/Account/Login", RedirectLocation(context));
    }

    #endregion

    #region Other paths

    [Fact]
    public async Task UnmatchedPath_InvokesNextMiddleware()
    {
        // Arrange
        var nextInvoked = false;
        var middleware = new CookieLoginMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/some-other-path";

        // Act
        await middleware.Invoke(context, BuildSignInManager(), _tokens);

        // Assert
        Assert.True(nextInvoked);
    }

    #endregion
}
