using System.Collections.Concurrent;
using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.MembersArea.Components.Account;

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

    public static IDictionary<Guid, RecoveryCodeLoginInfo> RecoveryCodeLogins { get; private set; } =
        new ConcurrentDictionary<Guid, RecoveryCodeLoginInfo>();

    public async Task Invoke(HttpContext context, SignInManager<User> signinManager)
    {
        if (context.Request.Path == "/loginrecovery" && context.Request.Query.ContainsKey("key"))
        {
            var key = Guid.Parse(context.Request.Query["key"]!);
            var info = RecoveryCodeLogins[key];
            RecoveryCodeLogins.Remove(key);

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
            var key = Guid.Parse(context.Request.Query["key"]!);
            var info = TwoFactorLogins[key];
            TwoFactorLogins.Remove(key);

            var code = info.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var result = await signinManager.TwoFactorAuthenticatorSignInAsync(code, info.RememberMe, info.RememberMachine);

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
            var key = Guid.Parse(context.Request.Query["key"]!);
            var info = Logins[key];

            var result = await signinManager.PasswordSignInAsync(
                info.Email,
                info.Password,
                info.RememberMe,
                lockoutOnFailure: true
            );
            Logins.Remove(key);
            if (result.Succeeded)
            {
                context.Response.Redirect("/");
                return;
            }
            else if (result.RequiresTwoFactor)
            {
                var rememberMe = info.RememberMe ? "true" : "false";
                context.Response.Redirect($"/Account/LoginWith2fa?rememberMe={rememberMe}&returnUrl=%2F");
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
}
