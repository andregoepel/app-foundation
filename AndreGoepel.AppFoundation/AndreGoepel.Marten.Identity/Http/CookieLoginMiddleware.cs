using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Http;

public sealed record LoginInfo(
    string Email,
    string Password,
    bool RememberMe,
    string? ReturnUrl = null
);

public sealed record TwoFactorLoginInfo(
    string Code,
    bool RememberMe,
    bool RememberMachine,
    string? ReturnUrl
);

public sealed record RecoveryCodeLoginInfo(string Code, string? ReturnUrl);

public class CookieLoginMiddleware(RequestDelegate next)
{
    /// <summary>Default post-login destination when no explicit returnUrl is supplied.</summary>
    private const string _defaultRedirect = "/dashboard";

    public async Task Invoke(
        HttpContext context,
        SignInManager<User> signinManager,
        LoginTokenProtector tokens
    )
    {
        if (context.Request.Path == "/loginrecovery")
        {
            if (
                !tokens.TryUnprotect<RecoveryCodeLoginInfo>(
                    context.Request.Query["token"],
                    out var info
                )
            )
            {
                context.Response.Redirect("/Account/Login");
                return;
            }

            var code = info.Code.Replace(" ", string.Empty);
            var result = await signinManager.TwoFactorRecoveryCodeSignInAsync(code);

            if (result.Succeeded)
                context.Response.Redirect(info.ReturnUrl ?? _defaultRedirect);
            else if (result.IsLockedOut)
                context.Response.Redirect("/Account/Lockout");
            else
                context.Response.Redirect("/Account/LoginWithRecoveryCode?error=invalid");
            return;
        }
        else if (context.Request.Path == "/login2fa")
        {
            if (
                !tokens.TryUnprotect<TwoFactorLoginInfo>(
                    context.Request.Query["token"],
                    out var info
                )
            )
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
                context.Response.Redirect(info.ReturnUrl ?? _defaultRedirect);
            else if (result.IsLockedOut)
                context.Response.Redirect("/Account/Lockout");
            else
                context.Response.Redirect("/Account/LoginWith2fa?error=invalid");
            return;
        }
        else if (context.Request.Path == "/login")
        {
            if (!tokens.TryUnprotect<LoginInfo>(context.Request.Query["token"], out var info))
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
                context.Response.Redirect(info.ReturnUrl ?? _defaultRedirect);
            else if (result.RequiresTwoFactor)
            {
                var rememberMe = info.RememberMe ? "true" : "false";
                var returnUrl = Uri.EscapeDataString(info.ReturnUrl ?? _defaultRedirect);
                context.Response.Redirect(
                    $"/Account/LoginWith2fa?rememberMe={rememberMe}&returnUrl={returnUrl}"
                );
            }
            else if (result.IsLockedOut)
                context.Response.Redirect("/Account/Lockout");
            else
                context.Response.Redirect("/Account/Login");
            return;
        }
        else
        {
            await next.Invoke(context);
        }
    }
}
