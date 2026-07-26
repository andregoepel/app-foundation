using System.Net;
using System.Security.Cryptography.X509Certificates;
using AndreGoepel.AppFoundation.Hosting.DataProtection;
using AndreGoepel.AppFoundation.Hosting.Quartz;
using AndreGoepel.AppFoundation.MailService;
using AndreGoepel.Design.Blazor;
using AndreGoepel.Marten.Configuration;
using AndreGoepel.Marten.Identity;
using AndreGoepel.Marten.Identity.Blazor;
using AndreGoepel.Marten.Identity.Blazor.Features;
using AndreGoepel.Marten.Identity.Users;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
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

        // Docker/Kubernetes secrets (key-per-file); no-op when the directory is absent (e.g. local dev).
        if (!string.IsNullOrWhiteSpace(options.SecretsDirectory))
        {
            builder.Configuration.AddKeyPerFile(options.SecretsDirectory, optional: true);
        }

        // Production proxy CIDRs (unknown at build time) can be supplied via config, augmenting code.
        MergeForwardedHeaderConfiguration(builder.Configuration, options);

        // First-run default-role ladder can also be supplied via config (#103).
        MergeDefaultRolesConfiguration(builder.Configuration, options);

        // Read by UseAppFoundation to configure forwarded headers.
        builder.Services.AddSingleton(options);

        // Setup.razor (in AndreGoepel.AppFoundation) can't reference AppFoundationOptions without a circular
        // project reference, so the resolved roles are exposed via the dependency-free DefaultRole record (#103).
        builder.Services.AddSingleton<IReadOnlyCollection<DefaultRole>>(
            options.DefaultRoles.ToList()
        );

        // Hardened HSTS default (365-day max age, includeSubDomains, preload) replacing the framework's own (#124).
        builder.Services.AddHsts(hsts => ConfigureHsts(hsts, options));

        builder.AddServiceDefaults();

        builder.Services.AddMartenIdentity();
        builder.Services.AddMartenIdentityBlazor(identity =>
        {
            // AppFoundation default: self-service registration off unless a host opts in; 2FA/passkeys stay on (#49).
            identity.EnableUserRegistration = false;
            options.ConfigureIdentity?.Invoke(identity);
        });
        builder.Services.AddMartenIdentityCleanup();

        var connectionString =
            builder.Configuration.GetConnectionString(options.DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{options.DatabaseConnectionName}' not found."
            );

        // Default to additive-only (CreateOrUpdate) outside Development; a host can override, e.g. AutoCreate.None
        // for a least-privilege role with schema applied out-of-band. Shared with Quartz's provisioning below (#53).
        var schemaCreation =
            options.SchemaCreation
            ?? (builder.Environment.IsDevelopment() ? AutoCreate.All : AutoCreate.CreateOrUpdate);

        // Merges into the same QuartzOptions as AddMartenIdentityCleanup's own AddQuartz call, layering a
        // Postgres-backed persistent store on top of its RAMJobStore registration. Configuration only — no I/O,
        // no re-registering jobs — so AddAppFoundation stays testable without a reachable database; the qrtz_
        // schema itself is provisioned later in UseAppFoundation (#129).
        builder.Services.AddQuartz(quartz =>
        {
            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);

                // Postgres folds unquoted identifiers to lowercase; Quartz's own default ("QRTZ_") would 404.
                store.SetProperty("quartz.jobStore.tablePrefix", "qrtz_");

                // Quartz's default BinaryObjectSerializer uses BinaryFormatter, removed in .NET 8+.
                store.UseSystemTextJsonSerializer();
            });
        });

        builder.Services.AddScoped<IEmailSender<User>, IdentityEmailSender>();

        var martenConfiguration = builder.Services.AddMarten(marten =>
        {
            marten.Connection(connectionString);

            marten.InitializeIdentity();

            marten.AutoCreateSchemaObjects = schemaCreation;

            // The alias (and table name) is part of the storage contract — existing key ring rows must
            // resolve under the same name on upgrade.
            marten
                .Schema.For<DataProtectionKeyDocument>()
                .DocumentAlias("dataprotectionkeydocument");

            // Every admin-configured settings record shares one table; consuming apps register their own
            // via AddSettingsDocument<T>().
            marten.AddSettingsDocument<EmailSettingsDocument>();
        });

        // Off by default (no behavior change for existing consumers); a host opts in via
        // AppFoundationOptions.EnableAsyncDaemon when it has async projections/subscriptions to run.
        if (options.EnableAsyncDaemon)
        {
            martenConfiguration.AddAsyncDaemon(options.AsyncDaemonMode);
        }

        martenConfiguration.IntegrateWithWolverine();

        builder.Services.AddMemoryCache();
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddScoped<NotificationService>();

        builder.Host.UseWolverine(wolverine =>
        {
            wolverine.ServiceName = options.WolverineServiceName;

            wolverine.Policies.UseDurableInboxOnAllListeners();
            wolverine.Policies.UseDurableOutboxOnAllSendingEndpoints();

            wolverine.Discovery.IncludeAssembly(typeof(SendEmailMessageHandler).Assembly);

            // The host owns the one allowed UseWolverine call; this runs inside it so config is applied
            // deterministically before handler discovery.
            options.ConfigureWolverine?.Invoke(wolverine);
        });

        builder.AddEmailService();

        AddDataProtection(builder, options);

        builder.Services.AddRadzenComponents();

        // AddMartenIdentityBlazor already seeds DesignBlazorOptions.BrandName from ApplicationName; configuring
        // here runs later and wins, so dashboard/login/account pages share AppFoundationLayoutOptions.BrandName.
        builder
            .Services.AddDesignBlazor()
            .AddOptions<DesignBlazorOptions>()
            .Configure<IOptions<AppFoundationLayoutOptions>>(
                (design, layout) => design.BrandName = layout.Value.BrandName
            );

        builder.Services.AddHeaderPropagation();

        return builder;
    }

    // Keys persist in Postgres via Marten and are encrypted at rest when a certificate is configured; without
    // DataProtection:CertificatePath (e.g. local dev) keys are stored unencrypted and ASP.NET Core logs a warning.
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

        var options = app.Services.GetRequiredService<AppFoundationOptions>();

        // Idempotent qrtz_ provisioning must happen here, not in AddAppFoundation, which stays side-effect-free
        // against the connection string so it's testable without a reachable database (#129).
        var connectionString =
            app.Configuration.GetConnectionString(options.DatabaseConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{options.DatabaseConnectionName}' not found."
            );
        var schemaCreation =
            options.SchemaCreation
            ?? (app.Environment.IsDevelopment() ? AutoCreate.All : AutoCreate.CreateOrUpdate);

        if (QuartzSchemaProvisioner.ShouldProvision(schemaCreation))
        {
            QuartzSchemaProvisioner.Provision(connectionString);
        }

        EnsureKeyRingProtected(
            app.Environment.IsDevelopment(),
            options.AllowUnprotectedKeyRing,
            app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor
        );

        var forwardedOptions = BuildForwardedHeadersOptions(
            options,
            app.Environment.IsDevelopment()
        );
        options.ConfigureForwardedHeaders?.Invoke(forwardedOptions);

        if (
            !app.Environment.IsDevelopment()
            && options.KnownProxyNetworks.Count == 0
            && options.KnownProxies.Count == 0
            && options.ConfigureForwardedHeaders is null
        )
        {
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(Initialization).FullName!)
                .LogWarning(
                    "AppFoundation: no reverse-proxy networks configured; X-Forwarded-* "
                        + "headers are honored only from loopback. If the app runs behind a "
                        + "reverse proxy, set AppFoundationOptions.KnownProxyNetworks / "
                        + "KnownProxies so the client IP and scheme are trusted from it."
                );
        }

        app.UseForwardedHeaders(forwardedOptions);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();

            app.Use(
                (context, next) =>
                {
                    ApplySecurityHeaders(context.Response.Headers, options);
                    return next();
                }
            );
        }

        // 404s that match no endpoint never reach the Blazor router, so re-execute them against /not-found; needs
        // its own DI scope because re-rendering in the original scope throws on an already-initialized
        // RemoteNavigationManager.
        app.UseStatusCodePagesWithReExecute(
            "/not-found",
            "?code={0}",
            createScopeForStatusCodePages: true
        );

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseHeaderPropagation();

        // Must run before anything that renders user-facing text — identity middleware below, and
        // MapRazorComponents — since a Blazor Server circuit takes its culture from the request that creates it.
        app.UseDesignBlazorLocalization();

        app.UseAntiforgery();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMartenIdentityMiddleware();

        // Disabled identity features (registration/2FA/passkeys) are unreachable by direct URL, not just hidden.
        app.UseMartenIdentityFeatureGate();

        return app;
    }

    // Throws unless the key ring is encrypted (or AllowUnprotectedKeyRing is set) outside Development — a DB
    // dump must not also yield the keys protecting the SMTP password, login tokens, and auth cookies (#54).
    internal static void EnsureKeyRingProtected(
        bool isDevelopment,
        bool allowUnprotectedKeyRing,
        IXmlEncryptor? xmlEncryptor
    )
    {
        if (isDevelopment || allowUnprotectedKeyRing || xmlEncryptor is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            "The DataProtection key ring would be stored unencrypted in the database, "
                + "where a dump would also expose the keys that protect the SMTP password, "
                + "login tokens, and auth cookies. Configure key encryption — set "
                + "DataProtection:CertificatePath, or use AppFoundationOptions."
                + "ConfigureDataProtection for Azure Key Vault / KMS — or set "
                + "AppFoundationOptions.AllowUnprotectedKeyRing = true to accept this "
                + "(e.g. when the database storage is encrypted at rest by other means)."
        );
    }

    // Merges AppFoundation:KnownProxyNetworks/KnownProxies from config (delimited scalar or array) into options,
    // augmenting whatever's set in code.
    internal static void MergeForwardedHeaderConfiguration(
        IConfiguration configuration,
        AppFoundationOptions options
    )
    {
        MergeInto(configuration, "AppFoundation:KnownProxyNetworks", options.KnownProxyNetworks);
        MergeInto(configuration, "AppFoundation:KnownProxies", options.KnownProxies);

        static void MergeInto(IConfiguration configuration, string key, IList<string> target)
        {
            foreach (var value in ReadDelimitedOrArray(configuration, key))
            {
                if (!target.Contains(value))
                {
                    target.Add(value);
                }
            }
        }

        static IEnumerable<string> ReadDelimitedOrArray(IConfiguration configuration, string key)
        {
            var section = configuration.GetSection(key);

            // Array form (appsettings.json arrays, or KEY__0 / KEY__1 env vars).
            var children = section.GetChildren().ToList();
            if (children.Count > 0)
            {
                return children
                    .Select(child => child.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim());
            }

            // Scalar/delimited form (single env var/.env entry); split on comma/semicolon/whitespace only,
            // never ':', so IPv6 CIDRs like fd00::/8 stay intact.
            return section.Value is { Length: > 0 } scalar
                ? scalar.Split(
                    [',', ';', ' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                : [];
        }
    }

    // Merges AppFoundation:DefaultRoles from config into options.DefaultRoles; roles already present by name
    // are left as-is (#103).
    internal static void MergeDefaultRolesConfiguration(
        IConfiguration configuration,
        AppFoundationOptions options
    )
    {
        var configuredRoles =
            configuration.GetSection("AppFoundation:DefaultRoles").Get<List<DefaultRole>>() ?? [];

        foreach (var role in configuredRoles)
        {
            if (options.DefaultRoles.All(existing => existing.Name != role.Name))
            {
                options.DefaultRoles.Add(role);
            }
        }
    }

    // Trusts X-Forwarded-For/Proto only from configured proxies, or any origin in Development; otherwise keeps
    // the framework's loopback-only default so arbitrary clients can't spoof the client IP/scheme (#51).
    internal static ForwardedHeadersOptions BuildForwardedHeadersOptions(
        AppFoundationOptions options,
        bool isDevelopment
    )
    {
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };

        if (options.KnownProxyNetworks.Count > 0 || options.KnownProxies.Count > 0)
        {
            forwardedOptions.KnownIPNetworks.Clear();
            forwardedOptions.KnownProxies.Clear();
            foreach (var network in options.KnownProxyNetworks)
            {
                forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
            foreach (var proxy in options.KnownProxies)
            {
                forwardedOptions.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        }
        else if (isDevelopment)
        {
            forwardedOptions.KnownIPNetworks.Clear();
            forwardedOptions.KnownProxies.Clear();
        }

        return forwardedOptions;
    }

    // Hardened HSTS defaults (365-day max age, includeSubDomains, preload) before the host's own override (#124).
    internal static void ConfigureHsts(HstsOptions hsts, AppFoundationOptions options)
    {
        hsts.MaxAge = TimeSpan.FromDays(365);
        hsts.IncludeSubDomains = true;
        hsts.Preload = true;

        options.ConfigureHsts?.Invoke(hsts);
    }

    // Sets security headers the framework leaves absent by default; Referrer-Policy/Permissions-Policy only
    // when configured, not cleared to null (#124).
    internal static void ApplySecurityHeaders(
        IHeaderDictionary headers,
        AppFoundationOptions options
    )
    {
        headers["X-Content-Type-Options"] = "nosniff";

        if (options.ReferrerPolicy is { Length: > 0 } referrerPolicy)
        {
            headers["Referrer-Policy"] = referrerPolicy;
        }

        if (options.PermissionsPolicy is { Length: > 0 } permissionsPolicy)
        {
            headers["Permissions-Policy"] = permissionsPolicy;
        }
    }
}
