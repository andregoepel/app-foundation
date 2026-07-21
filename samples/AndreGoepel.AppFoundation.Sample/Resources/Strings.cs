namespace AndreGoepel.AppFoundation.Sample.Resources;

/// <summary>
/// Marker type for the sample app's own UI strings — the generic argument of
/// <c>IStringLocalizer&lt;Strings&gt;</c>. Kept separate from the RCL's
/// <c>AppFoundationStrings</c>: the host translates its own content independently of
/// the identity/foundation UI it consumes.
/// </summary>
public sealed class Strings;
