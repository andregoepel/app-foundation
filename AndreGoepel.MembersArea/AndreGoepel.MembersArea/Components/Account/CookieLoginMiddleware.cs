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

public class CookieLoginMiddleware(RequestDelegate next)
{
    public static IDictionary<Guid, LoginInfo> Logins { get; private set; } =
        new ConcurrentDictionary<Guid, LoginInfo>();

    public async Task Invoke(HttpContext context, SignInManager<User> signinManager)
    {
        if (context.Request.Path == "/login" && context.Request.Query.ContainsKey("key"))
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
