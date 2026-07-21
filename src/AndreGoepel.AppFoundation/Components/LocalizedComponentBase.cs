using AndreGoepel.AppFoundation.Resources;
using Microsoft.AspNetCore.Components;

namespace AndreGoepel.AppFoundation.Components;

/// <summary>
/// Base for AppFoundation UI components that render their own text via <see cref="T(string)"/>
/// instead of a page-local copy of the same helper.
/// </summary>
/// <remarks>
/// A component must not <c>@inject IStringLocalizer&lt;AppFoundationStrings&gt;</c> directly:
/// that is a required injection, so rendering it throws on any host — or bUnit test — that
/// never called <c>AddAppFoundation</c>. Resolving through <see cref="IServiceProvider"/>
/// instead avoids that, which is why every translated page needs the same pair of methods;
/// this base class is the one place that pair is defined. Inherit it with <c>@inherits
/// LocalizedComponentBase</c> rather than repeating <c>@inject IServiceProvider Services</c> +
/// the two <c>T</c> overloads in each page.
/// <para>
/// Public rather than internal: the Razor compiler generates a routable (<c>@page</c>)
/// component's partial class as public, and a public class cannot derive from an internal
/// base (CS0060). Not intended for use outside this assembly regardless.
/// </para>
/// </remarks>
public abstract class LocalizedComponentBase : ComponentBase
{
    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    /// <summary>Looks up <paramref name="key"/> for the current UI culture.</summary>
    protected string T(string key) => Services.AppFoundationText(key);

    /// <inheritdoc cref="T(string)"/>
    /// <remarks>Formats the resolved string with <paramref name="arguments"/>.</remarks>
    protected string T(string key, params object[] arguments) =>
        Services.AppFoundationText(key, arguments);
}
