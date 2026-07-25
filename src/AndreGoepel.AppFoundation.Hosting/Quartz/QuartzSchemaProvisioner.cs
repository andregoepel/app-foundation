using System.Reflection;
using JasperFx;
using Npgsql;

namespace AndreGoepel.AppFoundation.Hosting.Quartz;

/// <summary>
/// Idempotently provisions Quartz's PostgreSQL job-store schema (<c>qrtz_*</c> tables) at
/// startup, mirroring Marten's own schema-creation posture — a fresh database must come up
/// with no manual steps, and a host that provisions schema out-of-band
/// (<see cref="AutoCreate.None"/>) skips this too (#129).
/// </summary>
internal static class QuartzSchemaProvisioner
{
    private const string ScriptResourceName =
        "AndreGoepel.AppFoundation.Hosting.Quartz.qrtz_tables_postgres.sql";

    /// <summary>
    /// Whether the schema should be provisioned for the given (already-resolved)
    /// <see cref="AutoCreate"/> mode — the same mode <c>AddAppFoundation</c> passes to
    /// Marten's <c>AutoCreateSchemaObjects</c>.
    /// </summary>
    internal static bool ShouldProvision(AutoCreate schemaCreation) =>
        schemaCreation != AutoCreate.None;

    /// <summary>
    /// Runs the vendored, idempotent DDL script against <paramref name="connectionString"/>.
    /// Synchronous and blocking by design: it runs once, during <c>AddAppFoundation</c>,
    /// before <c>WebApplicationBuilder.Build()</c> — well before Quartz's own hosted service
    /// starts and queries these tables, so there is no ordering-dependent async startup step
    /// to get wrong.
    /// </summary>
    internal static void Provision(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var command = new NpgsqlCommand(ReadScript(), connection);
        command.ExecuteNonQuery();
    }

    internal static string ReadScript()
    {
        using var stream =
            typeof(QuartzSchemaProvisioner).Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ScriptResourceName}' not found."
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
