using JasperFx;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Wolverine;

namespace AndreGoepel.AppFoundation.Hosting;

/// <summary>
/// Consumer-facing knobs for the AppFoundation backend seam. Defaults match the
/// in-repo host; downstream apps override the connection name and Wolverine
/// service name as needed.
/// </summary>
public sealed class AppFoundationOptions
{
    /// <summary>
    /// Name of the connection string (in configuration) for the Marten/PostgreSQL store.
    /// </summary>
    public string DatabaseConnectionName { get; set; } = "appfoundation-database";

    /// <summary>
    /// Wolverine service name used for the durable inbox/outbox endpoints.
    /// </summary>
    public string WolverineServiceName { get; set; } = "AppFoundation";

    /// <summary>
    /// Directory scanned for Docker/Kubernetes secrets (key-per-file). Each file's
    /// name becomes a configuration key (with <c>__</c> as the section separator), so a
    /// secret named <c>ConnectionStrings__appfoundation-database</c> supplies the
    /// connection string. Loaded with <c>optional: true</c>, so it is a no-op when the
    /// directory is absent (e.g. local development). Set to <c>null</c> to disable.
    /// </summary>
    public string? SecretsDirectory { get; set; } = "/run/secrets";

    /// <summary>
    /// DataProtection application discriminator, isolating this app's protected
    /// payloads from other apps sharing infrastructure. Defaults to
    /// <see cref="WolverineServiceName"/> when <c>null</c>.
    /// </summary>
    public string? DataProtectionApplicationDiscriminator { get; set; }

    /// <summary>
    /// Optional extension point on the DataProtection builder, invoked after the
    /// foundation's defaults (Marten-persisted key ring, optional certificate
    /// encryption from configuration). Use it for host-specific key protection
    /// such as Azure Key Vault, or for certificate rotation via
    /// <c>UnprotectKeysWithAnyCertificate</c>.
    /// </summary>
    public Action<IDataProtectionBuilder>? ConfigureDataProtection { get; set; }

    /// <summary>
    /// Optional extension point on the Wolverine options, invoked inside the
    /// foundation's single <c>UseWolverine</c> call after its own configuration.
    /// The host owns the one allowed <c>UseWolverine</c>, so consuming apps use
    /// this to contribute Wolverine setup — most commonly opting their handler
    /// assemblies into discovery, e.g.
    /// <c>options.ConfigureWolverine = w =&gt; w.Discovery.IncludeAssembly(typeof(SomeHandler).Assembly)</c>.
    /// </summary>
    public Action<WolverineOptions>? ConfigureWolverine { get; set; }

    /// <summary>
    /// How Marten manages the database schema. When <c>null</c> (the default) the
    /// foundation selects a safe mode by environment: <see cref="AutoCreate.All"/> in
    /// Development (fast inner loop; permits destructive rebuilds) and
    /// <see cref="AutoCreate.CreateOrUpdate"/> everywhere else (additive only — never
    /// drops or rewrites existing objects, so a code/database mismatch cannot destroy
    /// data at runtime).
    /// <para>
    /// Set explicitly to take control — most notably <see cref="AutoCreate.None"/> for a
    /// least-privilege deployment where the schema is provisioned out-of-band (a
    /// migration job / <c>db-apply</c>) and the application runs with a database role
    /// that has no DDL rights.
    /// </para>
    /// </summary>
    public AutoCreate? SchemaCreation { get; set; }

    /// <summary>
    /// Reverse-proxy networks (CIDR, e.g. <c>10.0.0.0/8</c>) whose
    /// <c>X-Forwarded-*</c> headers are trusted. Configure this (and/or
    /// <see cref="KnownProxies"/>) when the app runs behind a reverse proxy so the
    /// real client IP and scheme are honored only from that proxy — never from
    /// arbitrary clients. When both are empty the foundation trusts forwarded
    /// headers from any origin in Development (local convenience) but only from
    /// loopback otherwise, so a client cannot spoof <c>X-Forwarded-For</c>/
    /// <c>-Proto</c> in production (#51).
    /// </summary>
    public IList<string> KnownProxyNetworks { get; } = new List<string>();

    /// <summary>
    /// Individual reverse-proxy IP addresses whose <c>X-Forwarded-*</c> headers are
    /// trusted. See <see cref="KnownProxyNetworks"/> for the trust model.
    /// </summary>
    public IList<string> KnownProxies { get; } = new List<string>();

    /// <summary>
    /// Optional extension point on the <see cref="ForwardedHeadersOptions"/>, invoked
    /// after the foundation applies <see cref="KnownProxyNetworks"/>/
    /// <see cref="KnownProxies"/> and its environment defaults. Use it for full
    /// control — e.g. adjusting <c>ForwardLimit</c> or trusting all proxies when an
    /// upstream ingress is guaranteed to sanitize the headers.
    /// </summary>
    public Action<ForwardedHeadersOptions>? ConfigureForwardedHeaders { get; set; }
}
