using AndreGoepel.AppFoundation.Resources;
using AndreGoepel.Design.Blazor.Components;

namespace AndreGoepel.AppFoundation.Components;

/// <summary>
/// Thin non-generic wrapper over <see cref="LocalizedComponentBase{TMarker}"/> from
/// <c>AndreGoepel.Design.Blazor</c>, closed over <see cref="AppFoundationStrings"/>, so
/// <c>@inherits</c> doesn't need the closed generic name spelled out on every page.
/// </summary>
/// <remarks>
/// Not named <c>LocalizedComponentBase</c>: <c>AndreGoepel.Design.Blazor.Components</c> is
/// itself globally imported via <c>_Imports.razor</c> and already defines a non-generic
/// <c>LocalizedComponentBase</c> closed over its own <c>DesignStrings</c> — a shared name would
/// be ambiguous (CS0104) wherever both namespaces are in scope.
/// <para>
/// Public rather than internal: the Razor compiler generates a routable (<c>@page</c>)
/// component's partial class as public, and a public class cannot derive from an internal
/// base (CS0060). Not intended for use outside this assembly regardless.
/// </para>
/// </remarks>
public abstract class AppFoundationLocalizedComponentBase
    : LocalizedComponentBase<AppFoundationStrings>;
