using System.Security.Cryptography.X509Certificates;
using AndreGoepel.AppFoundation.Hosting.DataProtection;
using AndreGoepel.AppFoundation.MailService;
using AndreGoepel.Marten.Identity;
using AndreGoepel.Marten.Identity.Blazor;
using AndreGoepel.Marten.Identity.Users;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
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

        // Load Docker/Kubernetes secrets (key-per-file) so sensitive configuration —
        // e.g. the connection string — can be supplied as files under the secrets
        // directory instead of plaintext environment variables. No-op when the
        // directory is absent (optional: true), so local development is unaffected.
        if (!string.IsNullOrWhiteSpace(options.SecretsDirectory))
        {
            builder.Configuration.AddKeyPerFile(options.SecretsDirectory, optional: true);
        }

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

                // The alias (and thus the table name) is part of the storage
                // contract — hosts that persisted key ring entries with an
                // identically-shaped document keep their keys on upgrade.
                marten
                    .Schema.For<DataProtectionKeyDocument>()
                    .DocumentAlias("dataprotectionkeydocument");
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

            // Consuming apps contribute Wolverine setup here — the host owns the
            // one allowed UseWolverine call. Typically opting handler assemblies
            // into discovery. Runs inside the UseWolverine lambda so it is applied
            // deterministically before handler discovery.
            options.ConfigureWolverine?.Invoke(wolverine);
        });

        builder.AddEmailService();

        AddDataProtection(builder, options);

        builder.Services.AddRadzenComponents();

        builder.Services.AddHeaderPropagation();

        return builder;
    }

    /// <summary>
    /// DataProtection with a durable key ring: keys are persisted in Postgres via
    /// Marten (surviving container rebuilds) and — when a certificate is
    /// configured — encrypted at rest, so a database dump alone cannot decrypt
    /// <c>IDataProtector</c>-protected payloads. Without
    /// <c>DataProtection:CertificatePath</c> (e.g. local development) keys are
    /// stored unencrypted and ASP.NET Core logs its at-rest warning.
    /// </summary>
    private static void AddDataProtection(
        WebApplicationBuilder builder,
        AppFoundationOptions options
    )
    {
        builder.Services.Configure<DataProtectionOptions>(dataProtection =>
            dataProtection.ApplicationDiscriminator =
                options.DataProtectionApplicationDiscriminator ?? options.WolverineServiceName
        );

        builder
            .Services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>(
                (keyManagement, provider) =>
                    keyManagement.XmlRepository = new MartenXmlRepository(provider)
            );

        var dataProtectionBuilder = builder.Services.AddDataProtection();

        var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                builder.Configuration["DataProtection:CertificatePassword"]
            );
            dataProtectionBuilder.ProtectKeysWithCertificate(certificate);
        }

        options.ConfigureDataProtection?.Invoke(dataProtectionBuilder);
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
