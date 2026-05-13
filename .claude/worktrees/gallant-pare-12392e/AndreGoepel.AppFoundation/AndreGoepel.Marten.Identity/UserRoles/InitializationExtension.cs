using JasperFx.Events.Projections;
using Marten;

namespace AndreGoepel.Marten.Identity.UserRoles;

internal static class InitializationExtension
{
    public static void InitializeUserRolesStore(this StoreOptions options)
    {
        options
            .Schema.For<UserRoleAssignment>()
            .Identity(x => x.Id)
            .Duplicate(x => x.UserGuid)
            .Duplicate(x => x.RoleGuid);

        options.Projections.Add(new UserRoleAssignmentProjection(), ProjectionLifecycle.Inline);
    }
}
