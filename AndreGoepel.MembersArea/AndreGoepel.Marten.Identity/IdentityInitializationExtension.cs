using AndreGoepel.Marten.Identity.Users;
using JasperFx.Events.Projections;
using Marten;

namespace AndreGoepel.Marten.Identity;

public static class IdentityInitializationExtension
{
    public static void InitializeIdentity(this StoreOptions options)
    {
        options.InitializeUsersStore();
    }
}
