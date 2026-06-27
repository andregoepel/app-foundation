using Microsoft.AspNetCore.Components;

namespace AndreGoepel.AppFoundation;

/// <summary>
/// Branding and navigation extension points for the AppFoundation management shell.
/// Host apps configure these (e.g. <c>services.Configure&lt;AppFoundationLayoutOptions&gt;(...)</c>)
/// to brand the management area and contribute their own admin menu entries.
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
    /// Optional component type rendered inside the administrator section of the nav menu,
    /// letting a consumer add its own admin entries. Rendered via <see cref="DynamicComponent"/>.
    /// </summary>
    public Type? AdminMenu { get; set; }
}
