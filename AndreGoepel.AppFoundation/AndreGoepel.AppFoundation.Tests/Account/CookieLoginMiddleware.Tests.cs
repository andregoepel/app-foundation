using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.MembersArea.Components.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AndreGoepel.MembersArea.Tests.Account;

public class CookieLoginMiddlewareTests
{
    #region Helpers

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

    private static DefaultHttpContext BuildContext(string path, string key)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = QueryString.Create("key", key);
        return context;
    }

    private static CookieLoginMiddleware BuildMiddleware(bool nextCalled = false) =>
        new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

    private static string? RedirectLocation(DefaultHttpContext context) =>
        context.Response.Headers.Location.ToString();

    #endregion

    #region /login path

    [Fact]
    public async Task Login_Success_RedirectsToRoot()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.Logins[key] = new LoginInfo
        {
            Email = "alice@example.com",
            Password = "pw",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_RequiresTwoFactor_RedirectsToTwoFaPage()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.Logins[key] = new LoginInfo
        {
            Email = "alice@example.com",
            Password = "pw",
            RememberMe = false,
        };
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.TwoFactorRequired));
        var context = BuildContext("/login", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.StartsWith("/Account/LoginWith2fa", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_RequiresTwoFactor_IncludesRememberMeFlag()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.Logins[key] = new LoginInfo
        {
            Email = "alice@example.com",
            Password = "pw",
            RememberMe = true,
        };
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.TwoFactorRequired));
        var context = BuildContext("/login", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Contains("rememberMe=true", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_LockedOut_RedirectsToLockout()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.Logins[key] = new LoginInfo
        {
            Email = "alice@example.com",
            Password = "pw",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.LockedOut));
        var context = BuildContext("/login", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/Account/Lockout", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_Failed_RedirectsToLoginPage()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.Logins[key] = new LoginInfo
        {
            Email = "alice@example.com",
            Password = "wrong",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.Failed));
        var context = BuildContext("/login", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/Account/Login", RedirectLocation(context));
    }

    [Fact]
    public async Task Login_RemovesKeyFromDictionaryAfterProcessing()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.Logins[key] = new LoginInfo
        {
            Email = "alice@example.com",
            Password = "pw",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .PasswordSignInAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(Task.FromResult(SignInResult.Success));

        // Act
        await BuildMiddleware().Invoke(BuildContext("/login", key.ToString()), signInManager);

        // Assert
        Assert.False(CookieLoginMiddleware.Logins.ContainsKey(key));
    }

    #endregion

    #region /login2fa path

    [Fact]
    public async Task Login2fa_Success_RedirectsToReturnUrl()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.TwoFactorLogins[key] = new TwoFactorLoginInfo
        {
            Code = "123456",
            ReturnUrl = "/dashboard",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login2fa", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/dashboard", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_Success_NoReturnUrl_RedirectsToRoot()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.TwoFactorLogins[key] = new TwoFactorLoginInfo
        {
            Code = "123456",
            ReturnUrl = null,
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/login2fa", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_LockedOut_RedirectsToLockout()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.TwoFactorLogins[key] = new TwoFactorLoginInfo { Code = "123456" };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.LockedOut));
        var context = BuildContext("/login2fa", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/Account/Lockout", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_Failed_RedirectsWithErrorQuery()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.TwoFactorLogins[key] = new TwoFactorLoginInfo { Code = "wrong" };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Failed));
        var context = BuildContext("/login2fa", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/Account/LoginWith2fa?error=invalid", RedirectLocation(context));
    }

    [Fact]
    public async Task Login2fa_StripsSpacesAndDashesFromCode()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.TwoFactorLogins[key] = new TwoFactorLoginInfo
        {
            Code = "123 456-789",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorAuthenticatorSignInAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));

        // Act
        await BuildMiddleware().Invoke(BuildContext("/login2fa", key.ToString()), signInManager);

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
        var key = Guid.NewGuid();
        CookieLoginMiddleware.RecoveryCodeLogins[key] = new RecoveryCodeLoginInfo
        {
            Code = "ABCDE-FGHIJ",
            ReturnUrl = "/home",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/loginrecovery", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/home", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_Success_NoReturnUrl_RedirectsToRoot()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.RecoveryCodeLogins[key] = new RecoveryCodeLoginInfo
        {
            Code = "ABCDE-FGHIJ",
            ReturnUrl = null,
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Success));
        var context = BuildContext("/loginrecovery", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_LockedOut_RedirectsToLockout()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.RecoveryCodeLogins[key] = new RecoveryCodeLoginInfo { Code = "CODE" };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.LockedOut));
        var context = BuildContext("/loginrecovery", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/Account/Lockout", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_Failed_RedirectsWithErrorQuery()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.RecoveryCodeLogins[key] = new RecoveryCodeLoginInfo
        {
            Code = "wrong",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Failed));
        var context = BuildContext("/loginrecovery", key.ToString());

        // Act
        await BuildMiddleware().Invoke(context, signInManager);

        // Assert
        Assert.Equal("/Account/LoginWithRecoveryCode?error=invalid", RedirectLocation(context));
    }

    [Fact]
    public async Task LoginRecovery_StripsSpacesFromCode()
    {
        // Arrange
        var key = Guid.NewGuid();
        CookieLoginMiddleware.RecoveryCodeLogins[key] = new RecoveryCodeLoginInfo
        {
            Code = "ABC DEF GHI",
        };
        var signInManager = BuildSignInManager();
        signInManager
            .TwoFactorRecoveryCodeSignInAsync(Arg.Any<string>())
            .Returns(Task.FromResult(SignInResult.Success));

        // Act
        await BuildMiddleware()
            .Invoke(BuildContext("/loginrecovery", key.ToString()), signInManager);

        // Assert
        await signInManager.Received(1).TwoFactorRecoveryCodeSignInAsync("ABCDEFGHI");
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
        await middleware.Invoke(context, BuildSignInManager());

        // Assert
        Assert.True(nextInvoked);
    }

    #endregion
}
