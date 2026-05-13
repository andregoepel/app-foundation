using JasperFx.Events.Projections;
using Marten;

namespace AndreGoepel.Marten.Identity.Roles;

internal static class InitializationExtension
{
    public static void InitializeRolesStore(this StoreOptions options)
    {
        options.Schema.For<Role>().Identity(x => x.StreamId).Duplicate(x => x.NormalizedName);

        options.Projections.Add<RoleProjection>(ProjectionLifecycle.Inline);
    }
}
