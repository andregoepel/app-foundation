using AndreGoepel.Marten.Identity.Blazor;

namespace AndreGoepel.AppFoundation.Identity;

/// <summary>
/// Database-backed <see cref="IIdentityFeatureProvider"/> that serves the flags an
/// administrator persisted (falling back to the configuration baseline), replacing the
/// identity package's options-only default so the feature gate and UI honour runtime changes.
/// </summary>
internal sealed class MartenIdentityFeatureProvider(IIdentityFeatureSettingsStore store)
    : IIdentityFeatureProvider
{
    public async ValueTask<IdentityFeatureFlags> GetAsync(
        CancellationToken cancellationToken = default
    )
    {
        var settings = await store.LoadAsync(cancellationToken);
        return new IdentityFeatureFlags
        {
            UserRegistration = settings.EnableUserRegistration,
            TwoFactor = settings.EnableTwoFactor,
            Passkey = settings.EnablePasskey,
        };
    }
}
