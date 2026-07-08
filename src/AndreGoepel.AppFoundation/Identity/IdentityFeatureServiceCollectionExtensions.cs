using AndreGoepel.AppFoundation.Identity;
using AndreGoepel.Marten.Identity.Blazor.Features;

namespace Microsoft.Extensions.DependencyInjection;

public static class IdentityFeatureServiceCollectionExtensions
{
    /// <summary>
    /// Registers database-backed identity feature management: an
    /// <see cref="IIdentityFeatureSettingsStore"/> for the admin UI, and a
    /// <see cref="IIdentityFeatureProvider"/> that serves the persisted flags. Must be called
    /// after <c>AddMartenIdentityBlazor</c> so this provider replaces the options-only default
    /// (the last registration wins).
    /// </summary>
    public static IServiceCollection AddAppFoundationIdentityFeatures(
        this IServiceCollection services
    )
    {
        services.AddScoped<IIdentityFeatureSettingsStore, MartenIdentityFeatureSettingsStore>();
        services.AddScoped<IIdentityFeatureProvider, MartenIdentityFeatureProvider>();
        return services;
    }
}
