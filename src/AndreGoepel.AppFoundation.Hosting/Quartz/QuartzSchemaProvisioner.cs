using System.Reflection;
using JasperFx;
using Npgsql;

namespace AndreGoepel.AppFoundation.Hosting.Quartz;

// Idempotently provisions Quartz's PostgreSQL job-store schema (qrtz_* tables) at startup, mirroring Marten's
// own schema-creation posture; a host that provisions schema out-of-band (AutoCreate.None) skips this too (#129).
internal static class QuartzSchemaProvisioner
{
    private const string ScriptResourceName =
        "AndreGoepel.AppFoundation.Hosting.Quartz.qrtz_tables_postgres.sql";

    internal static bool ShouldProvision(AutoCreate schemaCreation) =>
        schemaCreation != AutoCreate.None;

    // Synchronous and blocking by design: runs once, before WebApplicationBuilder.Build(), well before Quartz's
    // own hosted service starts and queries these tables.
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
