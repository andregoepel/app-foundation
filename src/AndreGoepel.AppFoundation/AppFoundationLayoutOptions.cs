using Microsoft.AspNetCore.Components;

namespace AndreGoepel.AppFoundation;

/// <summary>
/// Branding and navigation extension points for the AppFoundation management shell.
/// Host apps configure these (e.g. <c>services.Configure&lt;AppFoundationLayoutOptions&gt;(...)</c>)
/// to brand the management area and contribute their own navigation entries.
/// </summary>
public sealed class AppFoundationLayoutOptions
{
    /// <summary>Brand/site name shown in the management sidebar.</summary>
    public string BrandName { get; set; } = "App Foundation";

    /// <summary>Path to the brand logo (defaults to the host's root <c>favicon.png</c>).</summary>
    public string LogoPath { get; set; } = "favicon.png";

    /// <summary>Footer line shown under the sidebar menu.</summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// Target of the sidebar's <c>Home</c> entry. Defaults to the packaged dashboard;
    /// hosts with their own landing page point this at it (e.g. <c>"/"</c>).
    /// </summary>
    public string HomeUrl { get; set; } = "dashboard";

    /// <summary>
    /// Optional component type whose entries are rendered in the navigation menu, immediately
    /// after <c>Home</c> and before the <c>Account</c> group. Lets a consumer contribute its own
    /// nav entries. Rendered via <see cref="DynamicComponent"/> for all users — the contributed
    /// component is responsible for gating any role-specific entries (e.g. with an
    /// <c>AuthorizeView</c>).
    /// </summary>
    public Type? Menu { get; set; }

    /// <summary>
    /// Obsolete alias for <see cref="Menu"/>. The contributed entries are no longer confined to
    /// the administrator section — they now render between <c>Home</c> and <c>Account</c> for all
    /// users — so the <c>Admin</c>-prefixed name no longer fits.
    /// </summary>
    [Obsolete(
        "Renamed to Menu. The contributed entries now render between Home and Account for all "
            + "users, not only administrators; gate role-specific entries inside your component. "
            + "Set Menu instead."
    )]
    public Type? AdminMenu
    {
        get => Menu;
        set => Menu = value;
    }
}
