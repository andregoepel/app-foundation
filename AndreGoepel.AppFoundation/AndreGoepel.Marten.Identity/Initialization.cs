using AndreGoepel.Marten.Identity.Http;
using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.UserRoles;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.Marten.Identity;

public static class Initialization
{
    public static IServiceCollection AddMartenIdentity(
        this IServiceCollection services,
        Action<IdentityOptions>? configureOptions = null
    )
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        services.AddAuthorization();

        services
            .AddIdentityCore<User>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                configureOptions?.Invoke(options);
            })
            .AddRoles<Role>()
            .AddUserManager<UserManager<User>>()
            .AddUserStore<UserStore<User>>()
            .AddRoleManager<RoleManager<Role>>()
            .AddRoleStore<RoleStore<Role>>()
            .AddDefaultTokenProviders()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }

    public static IApplicationBuilder UseMartenIdentityMiddleware(
        this IApplicationBuilder app
    )
    {
        app.UseMiddleware<SetupRedirectMiddleware>();
        app.UseMiddleware<CookieLoginMiddleware>();
        return app;
    }

    public static void InitializeIdentity(this global::Marten.StoreOptions options)
    {
        options.InitializeUsersStore();
        options.InitializeRolesStore();
        options.InitializeUserRolesStore();
    }
}
