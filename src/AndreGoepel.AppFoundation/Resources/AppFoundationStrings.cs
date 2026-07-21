namespace AndreGoepel.AppFoundation.Resources;

/// <summary>
/// Marker type for the AppFoundation UI's own strings — the generic argument of
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>. Kept separate from
/// the identity package's <c>IdentityStrings</c> and the design system's <c>DesignStrings</c>:
/// each layer translates its own content independently.
/// </summary>
public sealed class AppFoundationStrings;
