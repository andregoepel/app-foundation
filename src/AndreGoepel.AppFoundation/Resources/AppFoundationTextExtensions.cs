using System.Globalization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.AppFoundation.Resources;

/// <summary>
/// Resolves the AppFoundation UI's strings, tolerating a host that has not registered
/// localization.
/// </summary>
/// <remarks>
/// Pages must not <c>@inject IStringLocalizer&lt;AppFoundationStrings&gt;</c> directly: that
/// is a required injection, so rendering a page throws on any host — or bUnit test — that
/// never called <c>AddAppFoundation</c>. This library ships routable pages that consuming
/// apps render in their own tests, so the failure would land in code the consumer never
/// touched. Same reasoning, and same shape, as <c>IdentityTextExtensions</c> in
/// AndreGoepel.Marten.Identity.Blazor and <c>DesignTextExtensions</c> in
/// AndreGoepel.Design.Blazor.
/// </remarks>
internal static class AppFoundationTextExtensions
{
    // Same base name the IStringLocalizer path uses, so both routes read one resx pair and
    // no English text is duplicated in code.
    private static readonly ResourceManager Fallback = new(
        typeof(AppFoundationStrings).FullName!,
        typeof(AppFoundationStrings).Assembly
    );

    /// <summary>
    /// Looks <paramref name="key"/> up for the current UI culture. Prefers a registered
    /// <see cref="IStringLocalizer{T}"/> so a host can substitute one; otherwise reads the
    /// embedded resources directly.
    /// </summary>
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

        // CurrentUICulture is what request localization sets per request, so the fallback
        // stays culture-aware without any DI involvement.
        return Fallback.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    /// <inheritdoc cref="AppFoundationText(IServiceProvider, string)"/>
    internal static string AppFoundationText(
        this IServiceProvider services,
        string key,
        params object[] arguments
    ) => string.Format(CultureInfo.CurrentCulture, services.AppFoundationText(key), arguments);
}
