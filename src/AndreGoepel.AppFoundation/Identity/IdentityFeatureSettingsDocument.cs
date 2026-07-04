namespace AndreGoepel.AppFoundation.Identity;

/// <summary>
/// Marten document holding the single identity-feature-flag record. When present it takes
/// precedence over the configuration baseline, so an administrator can toggle the login
/// features at runtime without a redeploy.
/// </summary>
public sealed class IdentityFeatureSettingsDocument
{
    public const string DocumentId = "identity-feature-settings";

    public string Id { get; init; } = DocumentId;

    public bool EnableUserRegistration { get; init; } = true;

    public bool EnableTwoFactor { get; init; } = true;

    public bool EnablePasskey { get; init; } = true;
}
