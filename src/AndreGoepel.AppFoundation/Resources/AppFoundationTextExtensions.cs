using System.Globalization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.AppFoundation.Resources;

// Resolves the AppFoundation UI's strings, tolerating a host that hasn't registered localization: a required
// IStringLocalizer<AppFoundationStrings> injection would throw on any host (or bUnit test) that never called
// AddAppFoundation, since this library's routable pages render in consumers' own tests (same shape as
// IdentityTextExtensions in AndreGoepel.Marten.Identity.Blazor and DesignTextExtensions in AndreGoepel.Design.Blazor).
internal static class AppFoundationTextExtensions
{
    // Same base name the IStringLocalizer path uses, so both routes read one resx pair.
    private static readonly ResourceManager Fallback = new(
        typeof(AppFoundationStrings).FullName!,
        typeof(AppFoundationStrings).Assembly
    );

    // Prefers a registered IStringLocalizer<T> so a host can substitute one; otherwise reads embedded resources.
    internal static string AppFoundationText(this IServiceProvider services, string key)
    {
        if (services.GetService<IStringLocalizer<AppFoundationStrings>>() is { } localizer)
        {
            var localized = localizer[key];
            if (!localized.ResourceNotFound)
            {
                return localized.Value;
            }
        }

        // CurrentUICulture is set per request, so the fallback stays culture-aware without DI.
        return Fallback.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    // Same as above, with format arguments applied via string.Format.
    internal static string AppFoundationText(
        this IServiceProvider services,
        string key,
        params object[] arguments
    ) => string.Format(CultureInfo.CurrentCulture, services.AppFoundationText(key), arguments);
}
