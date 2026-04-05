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
                false,
                lockoutOnFailure: true
            );
            Logins.Remove(key);
            if (result.Succeeded)
            {
                Logins.Remove(key);
                context.Response.Redirect("/");
                return;
            }
            else if (result.RequiresTwoFactor)
            {
                //TODO: redirect to 2FA razor component
                context.Response.Redirect("/loginwith2fa/" + key);
                return;
            }
            else
            {
                //TODO: Proper error handling
                context.Response.Redirect("/loginfailed");
                return;
            }
        }
        else
        {
            await next.Invoke(context);
        }
    }
}
