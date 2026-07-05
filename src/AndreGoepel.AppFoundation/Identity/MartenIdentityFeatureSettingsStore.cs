using AndreGoepel.Marten.Identity.Blazor;
using Marten;
using Microsoft.Extensions.Options;

namespace AndreGoepel.AppFoundation.Identity;

/// <summary>
/// Database-first feature-flag resolution: the persisted record wins; without one the
/// <c>ConfigureIdentity</c> / <see cref="MartenIdentityBlazorOptions"/> baseline applies.
/// </summary>
internal sealed class MartenIdentityFeatureSettingsStore(
    IDocumentStore store,
    IOptions<MartenIdentityBlazorOptions> baseline
) : IIdentityFeatureSettingsStore
{
    public async Task<IdentityFeatureSettings> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var session = store.QuerySession();
        var document = await session.LoadAsync<IdentityFeatureSettingsDocument>(
            IdentityFeatureSettingsDocument.DocumentId,
            cancellationToken
        );
        if (document is not null)
        {
            return new IdentityFeatureSettings
            {
                EnableUserRegistration = document.EnableUserRegistration,
                EnableTwoFactor = document.EnableTwoFactor,
                EnablePasskey = document.EnablePasskey,
                FromConfiguration = false,
            };
        }

        var options = baseline.Value;
        return new IdentityFeatureSettings
        {
            EnableUserRegistration = options.EnableUserRegistration,
            EnableTwoFactor = options.EnableTwoFactor,
            EnablePasskey = options.EnablePasskey,
            FromConfiguration = true,
        };
    }

    public async Task SaveAsync(
        IdentityFeatureSettings settings,
        CancellationToken cancellationToken = default
    )
    {
        await using var session = store.LightweightSession();
        session.Store(
            new IdentityFeatureSettingsDocument
            {
                EnableUserRegistration = settings.EnableUserRegistration,
                EnableTwoFactor = settings.EnableTwoFactor,
                EnablePasskey = settings.EnablePasskey,
            }
        );
        await session.SaveChangesAsync(cancellationToken);
    }
}
