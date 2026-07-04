using System.Net;
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
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        // Let hosts declare trusted reverse proxies via configuration (environment
        // variables / .env / appsettings) in addition to code, so production proxy
        // CIDRs — unknown at build time — can be supplied at deploy time. Config
        // values augment anything set in the configure callback.
        MergeForwardedHeaderConfiguration(builder.Configuration, options);

        // Expose the resolved options to the request-pipeline side (UseAppFoundation),
        // which reads them to configure forwarded headers.
        builder.Services.AddSingleton(options);

        builder.AddServiceDefaults();

        builder.Services.AddMartenIdentity();
        builder.Services.AddMartenIdentityBlazor(options.ConfigureIdentity);
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

                // Never let the running app drop/rewrite schema to match code: default
                // to additive-only (CreateOrUpdate) outside Development, keeping the
                // permissive All only for the local inner loop. A host can override —
                // e.g. AutoCreate.None for a least-privilege role with schema applied
                // out-of-band (#53).
                marten.AutoCreateSchemaObjects =
                    options.SchemaCreation
                    ?? (
                        builder.Environment.IsDevelopment()
                            ? AutoCreate.All
                            : AutoCreate.CreateOrUpdate
                    );

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

        var options = app.Services.GetRequiredService<AppFoundationOptions>();

        // Fail closed if the key ring would be persisted unencrypted in a non-local
        // environment: the keys live in the same Postgres as the data they protect,
        // so a database dump must not also yield the keys (#54).
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
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseHeaderPropagation();
        app.UseAntiforgery();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMartenIdentityMiddleware();

        // Enforce the identity feature flags (registration / 2FA / passkeys) at the
        // request level: a disabled feature's pages and endpoints are unreachable by
        // direct URL, not merely hidden in the nav menu.
        app.UseMartenIdentityFeatureGate();

        return app;
    }

    /// <summary>
    /// Builds the forwarded-headers trust configuration. Honors <c>X-Forwarded-For</c>
    /// and <c>X-Forwarded-Proto</c>, but only from trusted origins: the configured
    /// proxy networks/proxies when supplied; otherwise every origin in Development
    /// (local convenience) and only the framework default (loopback) elsewhere, so
    /// arbitrary clients cannot spoof the client IP or scheme in production (#51).
    /// </summary>
    /// <summary>
    /// Throws when the DataProtection key ring would be stored without at-rest
    /// encryption outside Development, unless the host has explicitly accepted that
    /// via <see cref="AppFoundationOptions.AllowUnprotectedKeyRing"/>.
    /// <paramref name="xmlEncryptor"/> is the resolved
    /// <see cref="KeyManagementOptions.XmlEncryptor"/>: non-<c>null</c> whenever key
    /// encryption is configured (certificate, Key Vault, KMS, …), so this reflects the
    /// actual end state regardless of how protection was wired.
    /// </summary>
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

    /// <summary>
    /// Merges reverse-proxy trust configured under <c>AppFoundation:KnownProxyNetworks</c>
    /// and <c>AppFoundation:KnownProxies</c> into <paramref name="options"/>. Each key
    /// accepts either a delimited scalar (<c>"172.28.0.0/16, 10.0.0.0/8"</c> — friendly
    /// for a single environment variable / <c>.env</c>) or a configuration array, so the
    /// production proxy CIDRs can be supplied at deploy time without a code change.
    /// Values augment (and de-duplicate against) any set in code.
    /// </summary>
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

            // Scalar / delimited form (a single environment variable / .env entry).
            // Split on comma/semicolon/whitespace only — never ':' — so IPv6 CIDRs
            // such as fd00::/8 stay intact.
            return section.Value is { Length: > 0 } scalar
                ? scalar.Split(
                    [',', ';', ' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                : [];
        }
    }

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
            // Trust exactly the configured reverse proxies — and nothing else.
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
            // Local development: no proxy in front, so accept forwarded headers from
            // any origin for convenience.
            forwardedOptions.KnownIPNetworks.Clear();
            forwardedOptions.KnownProxies.Clear();
        }
        // Otherwise keep the framework defaults (loopback only): without a configured
        // proxy, arbitrary clients must not be able to spoof X-Forwarded-* headers.

        return forwardedOptions;
    }
}
