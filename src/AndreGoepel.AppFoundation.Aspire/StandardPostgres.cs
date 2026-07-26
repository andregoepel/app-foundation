using Aspire.Hosting.ApplicationModel;

namespace AndreGoepel.AppFoundation.Aspire;

/// <summary>
/// The two resources <see cref="AppFoundationAspireExtensions.AddStandardPostgres"/> creates.
/// Deconstructs positionally, so the common case that only needs the database stays a
/// one-liner: <c>var (_, appDb) = builder.AddStandardPostgres(isE2E);</c>
/// </summary>
/// <param name="Server">The Postgres server container resource.</param>
/// <param name="Database">
/// The logical database resource — what a host app passes to <c>WithReference</c>.
/// </param>
public sealed record StandardPostgres(
    IResourceBuilder<PostgresServerResource> Server,
    IResourceBuilder<PostgresDatabaseResource> Database
);
