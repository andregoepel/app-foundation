using JasperFx.Events.Projections;
using Marten;

namespace AndreGoepel.Marten.Identity.Users;

internal static class InitializationExtension
{
    public static void InitializeUsersStore(this StoreOptions options)
    {
        options.Schema.For<User>().Identity(x => x.UserId);
        //.Duplicate(x => x.Id)
        //.Duplicate(x => x.NormalizedEmail)
        //.Duplicate(x => x.NormalizedUserName);

        options.Projections.Add<UserProjection>(ProjectionLifecycle.Inline);
    }
}
