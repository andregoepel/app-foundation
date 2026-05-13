using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.UserRoles;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.Marten.Identity;

public static class Initialization
{
    public static void InitializeIdentityServices(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserService, CurrentUserService>();
    }

    public static void InitializeIdentity(this StoreOptions options)
    {
        options.InitializeUsersStore();
        options.InitializeRolesStore();
        options.InitializeUserRolesStore();
    }
}
