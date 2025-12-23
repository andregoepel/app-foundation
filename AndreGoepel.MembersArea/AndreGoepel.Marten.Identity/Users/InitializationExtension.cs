using JasperFx.Events.Projections;
using Marten;

namespace AndreGoepel.Marten.Identity.Users;

internal static class InitializationExtension
{
    public static void InitializeUsersStore(this StoreOptions options)
    {
        options.Schema.For<User>().Identity(x => x.UserId);
        options.Projections.Add<UserProjection>(ProjectionLifecycle.Inline);
    }
}
