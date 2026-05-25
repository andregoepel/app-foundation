using System.Collections.Concurrent;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Http;

public class LoginInfo
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public bool RememberMe { get; set; }
}

public class TwoFactorLoginInfo
{
    public required string Code { get; set; }
    public bool RememberMe { get; set; }
    public bool RememberMachine { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RecoveryCodeLoginInfo
{
    public required string Code { get; set; }
    public string? ReturnUrl { get; set; }
}

public class CookieLoginMiddleware(RequestDelegate next)
{
    public static IDictionary<Guid, LoginInfo> Logins { get; private set; } =
        new ConcurrentDictionary<Guid, LoginInfo>();

    public static IDictionary<Guid, TwoFactorLoginInfo> TwoFactorLogins { get; private set; } =
        new ConcurrentDictionary<Guid, TwoFactorLoginInfo>();

    public static IDictionary<Guid, RecoveryCodeLoginInfo> RecoveryCodeLogins
    {
        get;
        private set;
    } = new ConcurrentDictionary<Guid, RecoveryCodeLoginInfo>();

    public async Task Invoke(HttpContext context, SignInManager<User> signinManager)
    {
        if (context.Request.Path == "/loginrecovery" && context.Request.Query.ContainsKey("key"))
        {
            if (!TryConsumeKey(context, RecoveryCodeLogins, out var info))
            {
                context.Response.Redirect("/Account/Login");
                return;
            }

            var code = info.Code.Replace(" ", string.Empty);
            var result = await signinManager.TwoFactorRecoveryCodeSignInAsync(code);

            if (result.Succeeded)
            {
                context.Response.Redirect(info.ReturnUrl ?? "/");
            }
            else if (result.IsLockedOut)
            {
                context.Response.Redirect("/Account/Lockout");
            }
            else
            {
                context.Response.Redirect("/Account/LoginWithRecoveryCode?error=invalid");
            }
            return;
        }
        else if (context.Request.Path == "/login2fa" && context.Request.Query.ContainsKey("key"))
        {
            if (!TryConsumeKey(context, TwoFactorLogins, out var info))
            {
                context.Response.Redirect("/Account/Login");
                return;
            }

            var code = info.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var result = await signinManager.TwoFactorAuthenticatorSignInAsync(
                code,
                info.RememberMe,
                info.RememberMachine
            );

            if (result.Succeeded)
            {
                context.Response.Redirect(info.ReturnUrl ?? "/");
            }
            else if (result.IsLockedOut)
            {
                context.Response.Redirect("/Account/Lockout");
            }
            else
            {
                context.Response.Redirect("/Account/LoginWith2fa?error=invalid");
            }
            return;
        }
        else if (context.Request.Path == "/login" && context.Request.Query.ContainsKey("key"))
        {
            if (!TryConsumeKey(context, Logins, out var info))
            {
                context.Response.Redirect("/Account/Login");
                return;
            }

            var result = await signinManager.PasswordSignInAsync(
                info.Email,
                info.Password,
                info.RememberMe,
                lockoutOnFailure: true
            );
            if (result.Succeeded)
            {
                context.Response.Redirect("/");
                return;
            }
            else if (result.RequiresTwoFactor)
            {
                var rememberMe = info.RememberMe ? "true" : "false";
                context.Response.Redirect(
                    $"/Account/LoginWith2fa?rememberMe={rememberMe}&returnUrl=%2F"
                );
                return;
            }
            else if (result.IsLockedOut)
            {
                context.Response.Redirect("/Account/Lockout");
                return;
            }
            else
            {
                context.Response.Redirect("/Account/Login");
                return;
            }
        }
        else
        {
            await next.Invoke(context);
        }
    }

    private static bool TryConsumeKey<T>(
        HttpContext context,
        IDictionary<Guid, T> store,
        out T info
    )
    {
        if (
            !Guid.TryParse(context.Request.Query["key"], out var key)
            || !store.TryGetValue(key, out var existing)
        )
        {
            info = default!;
            return false;
        }

        store.Remove(key);
        info = existing;
        return true;
    }
}
