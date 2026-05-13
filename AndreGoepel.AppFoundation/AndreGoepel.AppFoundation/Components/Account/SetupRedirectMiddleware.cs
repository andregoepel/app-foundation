using AndreGoepel.Marten.Identity.Users;
using Marten;

namespace AndreGoepel.MembersArea.Components.Account;

public class SetupRedirectMiddleware(RequestDelegate next)
{
    private static volatile bool _isConfigured;

    public async Task Invoke(HttpContext context, IQuerySession querySession)
    {
        if (!_isConfigured)
        {
            var hasUsers = await querySession.Query<User>().AnyAsync();
            if (hasUsers)
            {
                _isConfigured = true;
            }
        }

        var path = context.Request.Path.Value ?? "";

        if (!_isConfigured && !IsSetupPath(path) && !IsInternalPath(path))
        {
            context.Response.Redirect("/Setup");
            return;
        }

        await next.Invoke(context);
    }

    private static bool IsSetupPath(string path) =>
        path.Equals("/Setup", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/Setup/", StringComparison.OrdinalIgnoreCase);

    private static bool IsInternalPath(string path) =>
        path.StartsWith("/_", StringComparison.Ordinal)
        || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
        || HasStaticExtension(path);

    private static bool HasStaticExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".css" or ".js" or ".png" or ".ico" or ".svg"
            or ".woff" or ".woff2" or ".ttf" or ".eot" or ".map" or ".webp";
    }
}
