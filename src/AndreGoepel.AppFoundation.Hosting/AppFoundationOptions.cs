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
}
