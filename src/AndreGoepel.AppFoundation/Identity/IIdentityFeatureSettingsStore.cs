namespace AndreGoepel.AppFoundation.Identity;

/// <summary>
/// Load/save seam for the database-backed identity feature flags, consumed by the
/// administration UI. The runtime <c>IIdentityFeatureProvider</c> resolves the same record,
/// so a save takes effect on the next request without an application restart.
/// </summary>
public interface IIdentityFeatureSettingsStore
{
    /// <summary>
    /// Returns the effective settings: the saved record when present, otherwise the
    /// configuration baseline with <see cref="IdentityFeatureSettings.FromConfiguration"/> set.
    /// </summary>
    Task<IdentityFeatureSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the settings, taking precedence over the configuration baseline.</summary>
    Task SaveAsync(IdentityFeatureSettings settings, CancellationToken cancellationToken = default);
}
