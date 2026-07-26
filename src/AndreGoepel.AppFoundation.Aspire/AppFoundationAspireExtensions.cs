using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AndreGoepel.AppFoundation.Aspire;

/// <summary>
/// Standardized AppHost building blocks for the AndreGoepel ecosystem, extracted from the
/// near-identical MailHog + Postgres + <c>EmailSender__*</c> blocks that <c>andregoepel-dev</c>,
/// <c>finance-app</c>, <c>customer-portal</c>, and this repo's own sample AppHost each hand-rolled.
/// AppHost projects are dev-only orchestration — never part of the runtime dependency chain — so a
/// sibling package referenced only by <c>*.AppHost</c> projects stays architecturally consistent
/// with the rest of the ecosystem's AppHosts already referencing
/// <c>AndreGoepel.AppFoundation.ServiceDefaults</c> from this same repo.
/// </summary>
public static class AppFoundationAspireExtensions
{
    /// <summary>
    /// Adds a MailHog container that captures outgoing development email locally: an SMTP
    /// endpoint and an HTTP endpoint for its web UI / API.
    /// </summary>
    /// <param name="builder">The AppHost's distributed application builder.</param>
    /// <param name="name">Resource name of the container.</param>
    /// <param name="tag">
    /// Image tag. Defaults to the pinned <c>v1.0.1</c> for reproducible dev environments; pass
    /// <c>null</c> to use the image's default (untagged/latest) instead.
    /// </param>
    /// <param name="smtpPort">Host port MailHog's SMTP endpoint is exposed on.</param>
    /// <param name="httpPort">Host port MailHog's HTTP (web UI / API) endpoint is exposed on.</param>
    /// <returns>
    /// The container resource, so callers can still reference its endpoints (e.g.
    /// <c>mailhog.GetEndpoint("smtp")</c>) to wire up their own app's <c>EmailSender__*</c>
    /// environment variables — those stay app-specific (sender name, credentials) and are not
    /// standardized here.
    /// </returns>
    /// <remarks>
    /// The HTTP endpoint is always named <c>"http"</c> — the canonical name across the ecosystem
    /// (matching <c>AndreGoepel.Testing.E2E</c>'s <c>E2EAppFixtureOptions.MailHogEndpointName</c>
    /// default), not the <c>"web"</c> name <c>andregoepel-dev</c>/<c>finance-app</c> used historically.
    /// </remarks>
    public static IResourceBuilder<ContainerResource> AddStandardMailHog(
        this IDistributedApplicationBuilder builder,
        string name = "mailhog",
        string? tag = "v1.0.1",
        int smtpPort = 1025,
        int httpPort = 8025
    )
    {
        var container = tag is null
            ? builder.AddContainer(name, "mailhog/mailhog")
            : builder.AddContainer(name, "mailhog/mailhog", tag);

        return container
            .WithEndpoint(name: "smtp", port: smtpPort, targetPort: 1025)
            .WithHttpEndpoint(name: "http", port: httpPort, targetPort: 8025);
    }

    /// <summary>
    /// Adds a Postgres server and database, applying the ecosystem's E2E convention: outside E2E
    /// the server keeps its data across restarts on a fixed host port; under E2E every one of
    /// those is dropped so each run gets a fresh, throwaway, volume-less database on a dynamic
    /// port instead of a developer's persistent local data.
    /// </summary>
    /// <param name="builder">The AppHost's distributed application builder.</param>
    /// <param name="isE2E">
    /// Whether the AppHost is running under the E2E test harness (typically
    /// <c>string.Equals(builder.Configuration["E2E"], "true", StringComparison.OrdinalIgnoreCase)</c>).
    /// <c>true</c> skips <c>WithDataVolume</c>, the persistent container lifetime, and the fixed
    /// host port.
    /// </param>
    /// <param name="serverName">Resource name of the Postgres server.</param>
    /// <param name="databaseResourceName">
    /// Resource name of the logical database — the connection-string name a host reads (e.g.
    /// <c>AppFoundationOptions.DatabaseConnectionName</c>, which defaults to
    /// <c>"appfoundation-database"</c>).
    /// </param>
    /// <param name="databaseName">
    /// Actual database name created on the server. Defaults to <paramref name="databaseResourceName"/>
    /// when <c>null</c>.
    /// </param>
    /// <param name="userName">Optional explicit database user parameter; auto-generated when omitted.</param>
    /// <param name="password">Optional explicit database password parameter; auto-generated when omitted.</param>
    /// <param name="hostPort">
    /// Fixed host port used outside E2E. Pass <c>null</c> to leave the port dynamic even outside
    /// E2E. Has no effect when <paramref name="isE2E"/> is <c>true</c>.
    /// </param>
    /// <param name="dataVolumeName">
    /// Optional explicit data volume name, forwarded to <c>WithDataVolume</c>. Auto-generated when
    /// <c>null</c>. Has no effect when <paramref name="isE2E"/> is <c>true</c>.
    /// </param>
    /// <returns>Both the server and database resources — the common case only needs <c>Database</c>.</returns>
    public static StandardPostgres AddStandardPostgres(
        this IDistributedApplicationBuilder builder,
        bool isE2E,
        string serverName = "postgres-server",
        string databaseResourceName = "appfoundation-database",
        string? databaseName = null,
        IResourceBuilder<ParameterResource>? userName = null,
        IResourceBuilder<ParameterResource>? password = null,
        int? hostPort = 5432,
        string? dataVolumeName = null
    )
    {
        var server =
            userName is not null && password is not null
                ? builder.AddPostgres(serverName, userName, password)
                : builder.AddPostgres(serverName);

        if (!isE2E)
        {
            server = server.WithLifetime(ContainerLifetime.Persistent);

            if (hostPort is { } port)
            {
                server = server.WithHostPort(port);
            }

            server = server.WithDataVolume(dataVolumeName);
        }

        var database = databaseName is null
            ? server.AddDatabase(databaseResourceName)
            : server.AddDatabase(databaseResourceName, databaseName);

        return new StandardPostgres(server, database);
    }
}
