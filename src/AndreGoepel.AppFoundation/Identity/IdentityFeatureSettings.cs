namespace AndreGoepel.AppFoundation.Identity;

/// <summary>
/// The identity login features as exposed to the administration UI: which of
/// self-service registration, two-factor authentication, and passkeys are enabled.
/// </summary>
public sealed record IdentityFeatureSettings
{
    public bool EnableUserRegistration { get; init; } = true;

    public bool EnableTwoFactor { get; init; } = true;

    public bool EnablePasskey { get; init; } = true;

    /// <summary>
    /// <c>true</c> when the values come from the configuration baseline
    /// (<c>ConfigureIdentity</c> / <c>MartenIdentityBlazorOptions</c>) because no record
    /// has been saved yet; <c>false</c> once an administrator persists them.
    /// </summary>
    public bool FromConfiguration { get; init; }
}
