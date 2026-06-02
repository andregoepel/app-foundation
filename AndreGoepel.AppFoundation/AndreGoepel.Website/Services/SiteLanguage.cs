using Microsoft.AspNetCore.Http;

namespace AndreGoepel.Website.Services;

/// <summary>
/// Resolves the site language for a request without any client round-trip, so the
/// landing page can be rendered statically in the correct language on first paint.
/// Precedence: explicit <c>ag-lang</c> cookie, then the browser's Accept-Language header.
/// </summary>
public static class SiteLanguage
{
    public const string CookieName = "ag-lang";

    public static string Resolve(HttpContext? context)
    {
        var cookie = context?.Request.Cookies[CookieName];
        if (cookie is "en" or "de")
            return cookie;

        var acceptLanguage = context?.Request.Headers.AcceptLanguage.ToString() ?? string.Empty;
        return acceptLanguage.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
    }
}
