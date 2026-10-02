using AndreGoepel.AppFoundation.Hosting.Quartz;
using AndreGoepel.Marten.Testing;
using Npgsql;
using Infrastructure = AndreGoepel.AppFoundation.IntegrationTests.Infrastructure;

namespace AndreGoepel.AppFoundation.IntegrationTests.Hosting;

[Collection(Infrastructure.IntegrationCollection.Name)]
public sealed class QuartzSchemaMigrationTests(MartenFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provision_FreshOrQuartz411Store_PreservesRowsAndSupportsRepeatedStartup(
        bool legacySchema
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = $"quartz_migration_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync(ct);
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
        {
            await create.ExecuteNonQueryAsync(ct);
        }

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                SearchPath = schema,
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);

            if (legacySchema)
            {
                using var stream =
                    typeof(QuartzSchemaMigrationTests).Assembly.GetManifestResourceStream(
                        "AndreGoepel.AppFoundation.IntegrationTests.Fixtures.Quartz411Schema.sql"
                    );
                Assert.NotNull(stream);
                using var reader = new StreamReader(stream);
                await using var baseline = new NpgsqlCommand(
                    await reader.ReadToEndAsync(ct),
                    connection
                );
                await baseline.ExecuteNonQueryAsync(ct);
            }
            else
            {
                QuartzSchemaProvisioner.Provision(connectionString);
            }

            await using (
                var seed = new NpgsqlCommand(
                    """
                    INSERT INTO qrtz_job_details
                        (sched_name, job_name, job_group, description, job_class_name,
                         is_durable, is_nonconcurrent, is_update_data, requests_recovery)
                    VALUES ('migration', 'existing-job', 'default', 'preserve-job',
                            'ExistingJob', TRUE, FALSE, FALSE, FALSE);
                    INSERT INTO qrtz_triggers
                        (sched_name, trigger_name, trigger_group, job_name, job_group,
                         description, next_fire_time, trigger_state, trigger_type, start_time)
                    VALUES ('migration', 'existing-trigger', 'default', 'existing-job', 'default',
                            'preserve-trigger', 123456789, 'WAITING', 'SIMPLE', 123456000);
                    """,
                    connection
                )
            )
            {
                await seed.ExecuteNonQueryAsync(ct);
            }

            QuartzSchemaProvisioner.Provision(connectionString);
            QuartzSchemaProvisioner.Provision(connectionString);

            await using (
                var trigger = new NpgsqlCommand(
                    "SELECT next_fire_time FROM qrtz_triggers WHERE trigger_name = 'existing-trigger' AND description = 'preserve-trigger'",
                    connection
                )
            )
            {
                Assert.Equal(123456789L, await trigger.ExecuteScalarAsync(ct));
            }
            await using (
                var job = new NpgsqlCommand(
                    "SELECT description FROM qrtz_job_details WHERE job_name = 'existing-job'",
                    connection
                )
            )
            {
                Assert.Equal("preserve-job", await job.ExecuteScalarAsync(ct));
            }

            // These columns are mandatory in Quartz 4.2/4.3, including pause metadata on both group tables.
            await using var columns = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = current_schema() AND is_nullable = 'YES' AND
                    ((table_name = 'qrtz_triggers' AND column_name IN
                        ('continues_trigger_name', 'continues_trigger_group', 'continuation_condition',
                         'overlap_policy', 'pause_reason', 'paused_by', 'paused_at'))
                     OR (table_name = 'qrtz_fired_triggers' AND column_name IN ('progress', 'progress_message'))
                     OR (table_name IN ('qrtz_paused_trigger_grps', 'qrtz_paused_job_grps')
                         AND column_name IN ('pause_reason', 'paused_by', 'paused_at')));
                """,
                connection
            );
            Assert.Equal(15L, await columns.ExecuteScalarAsync(ct));
        }
        finally
        {
            // Only the unique schema created in this test's disposable PostgreSQL container is removed.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
