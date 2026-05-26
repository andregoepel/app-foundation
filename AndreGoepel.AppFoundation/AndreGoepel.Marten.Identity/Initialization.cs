using AndreGoepel.Marten.Identity.Http;
using AndreGoepel.Marten.Identity.Roles;
using AndreGoepel.Marten.Identity.Services;
using AndreGoepel.Marten.Identity.UserRoles;
using AndreGoepel.Marten.Identity.Users;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

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
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<UserStore<User>>();
        services.AddScoped<RoleStore<Role>>();
        services.AddSingleton<Http.LoginTokenProtector>();

        return services;
    }

    public static IApplicationBuilder UseMartenIdentityMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<SetupRedirectMiddleware>();
        app.UseMiddleware<CookieLoginMiddleware>();
        return app;
    }

    public static IServiceCollection AddMartenIdentityCleanup(
        this IServiceCollection services,
        Action<DeletedUserCleanupOptions>? configure = null
    )
    {
        var opts = new DeletedUserCleanupOptions();
        configure?.Invoke(opts);
        services.Configure<DeletedUserCleanupOptions>(o =>
        {
            o.RetentionDays = opts.RetentionDays;
            o.CronSchedule = opts.CronSchedule;
        });

        var jobKey = new JobKey("DeletedUserCleanup", "MartenIdentity");
        services.AddQuartz(q =>
        {
            q.AddJob<DeletedUserCleanupJob>(j => j.WithIdentity(jobKey));
            q.AddTrigger(t =>
                t.ForJob(jobKey)
                    .WithIdentity("DeletedUserCleanupTrigger", "MartenIdentity")
                    .WithCronSchedule(opts.CronSchedule)
            );
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        services.AddScoped<CleanupSettingsService>();
        services.AddHostedService<CleanupScheduleStartupService>();

        return services;
    }

    public static void InitializeIdentity(this global::Marten.StoreOptions options)
    {
        options.InitializeUsersStore();
        options.InitializeRolesStore();
        options.InitializeUserRolesStore();
        options.Schema.For<CleanupSettings>();
    }
}
