using AndreGoepel.AppFoundation.MailService;
using AndreGoepel.Marten.Identity;
using AndreGoepel.Marten.Identity.Blazor;
using AndreGoepel.Marten.Identity.Users;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radzen;
using Wolverine;
using Wolverine.Marten;

namespace AndreGoepel.AppFoundation.Hosting;

/// <summary>
/// One-call backend seam for the AppFoundation: registers the data store, identity,
/// messaging, email, and UI services, and wires the shared request pipeline. Host
/// apps own their own root component, routing, and any consumer-specific services.
/// </summary>
public static class Initialization
{
    public static WebApplicationBuilder AddAppFoundation(
        this WebApplicationBuilder builder,
        Action<AppFoundationOptions>? configure = null
    )
    {
        var options = new AppFoundationOptions();
        configure?.Invoke(options);

        builder.AddServiceDefaults();

        builder.Services.AddMartenIdentity();
        builder.Services.AddMartenIdentityBlazor();
        builder.Services.AddMartenIdentityCleanup();

        var connectionString =
            builder.Configuration.GetConnectionString(options.DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{options.DatabaseConnectionName}' not found."
            );

        builder.Services.AddScoped<IEmailSender<User>, IdentityEmailSender>();

        builder
            .Services.AddMarten(marten =>
            {
                marten.Connection(connectionString);

                marten.InitializeIdentity();
                marten.AutoCreateSchemaObjects = AutoCreate.All;
            })
            .IntegrateWithWolverine();

        builder.Services.AddMemoryCache();
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddScoped<NotificationService>();

        builder.Host.UseWolverine(wolverine =>
        {
            wolverine.ServiceName = options.WolverineServiceName;

            wolverine.Policies.UseDurableInboxOnAllListeners();
            wolverine.Policies.UseDurableOutboxOnAllSendingEndpoints();

            wolverine.Discovery.IncludeAssembly(typeof(SendEmailMessageHandler).Assembly);
        });

        builder.AddEmailService();

        builder.Services.AddDataProtection();

        builder.Services.AddRadzenComponents();

        builder.Services.AddHeaderPropagation();

        return builder;
    }

    public static WebApplication UseAppFoundation(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        forwardedOptions.KnownIPNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedOptions);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseHeaderPropagation();
        app.UseAntiforgery();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMartenIdentityMiddleware();

        return app;
    }
}
