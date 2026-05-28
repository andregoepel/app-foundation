using Marten;
using Microsoft.AspNetCore.Http;

namespace AndreGoepel.Marten.Identity.Http;

public class SetupRedirectMiddleware(RequestDelegate next)
{
    private static volatile bool _isConfigured;

    public async Task Invoke(HttpContext context, IQuerySession querySession)
    {
        if (!_isConfigured && await SetupCompletion.IsCompleteAsync(querySession))
        {
            _isConfigured = true;
        }

        var path = context.Request.Path.Value ?? "";

        if (!_isConfigured && !IsSetupPath(path) && IsPageNavigation(context))
        {
            context.Response.Redirect("/Setup");
            return;
        }

        await next.Invoke(context);
    }

    private static bool IsSetupPath(string path) =>
        path.Equals("/Setup", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/Setup/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true only for browser page navigations (i.e. requests that should
    /// render a full HTML page). Sub-resource fetches for scripts, styles, images,
    /// fonts, and framework files are never redirected to /Setup.
    /// </summary>
    private static bool IsPageNavigation(HttpContext context)
    {
        // Modern browsers send Sec-Fetch-Dest with every request.
        // "document" and "iframe" are the only values that represent a full
        // page navigation; everything else (script, style, image, font, …) is
        // a sub-resource that should pass through regardless of setup state.
        var dest = context.Request.Headers["Sec-Fetch-Dest"].FirstOrDefault();
        if (!string.IsNullOrEmpty(dest))
            return dest is "document" or "iframe" or "embed" or "object";

        // Fallback for older clients that don't send Sec-Fetch-Dest:
        // page navigations always include "text/html" in the Accept header,
        // whereas script/style/image/fetch requests use "*/*" or a type-specific
        // value that never literally contains "text/html".
        return context.Request.Headers.Accept.Any(v =>
            v?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true
        );
    }
}
