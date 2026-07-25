using AndreGoepel.AppFoundation.Hosting.Quartz;
using JasperFx;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public sealed class QuartzSchemaProvisionerTests
{
    private static readonly string[] ExpectedTables =
    [
        "qrtz_job_details",
        "qrtz_triggers",
        "qrtz_simple_triggers",
        "qrtz_simprop_triggers",
        "qrtz_cron_triggers",
        "qrtz_blob_triggers",
        "qrtz_calendars",
        "qrtz_paused_trigger_grps",
        "qrtz_fired_triggers",
        "qrtz_scheduler_state",
        "qrtz_locks",
    ];

    [Theory]
    [InlineData(AutoCreate.All)]
    [InlineData(AutoCreate.CreateOrUpdate)]
    [InlineData(AutoCreate.CreateOnly)]
    public void ShouldProvision_AnyModeExceptNone_ReturnsTrue(AutoCreate autoCreate)
    {
        // Act / Assert
        Assert.True(QuartzSchemaProvisioner.ShouldProvision(autoCreate));
    }

    [Fact]
    public void ShouldProvision_None_ReturnsFalse()
    {
        // Arrange — AutoCreate.None means schema is provisioned out-of-band (#53); Quartz's
        // qrtz_ tables follow the same posture as Marten's schema.
        // Act / Assert
        Assert.False(QuartzSchemaProvisioner.ShouldProvision(AutoCreate.None));
    }

    [Fact]
    public void ReadScript_ContainsEveryExpectedTable_AsCreateIfNotExists()
    {
        // Act
        var script = QuartzSchemaProvisioner.ReadScript();

        // Assert
        foreach (var table in ExpectedTables)
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {table}", script);
        }
    }

    [Fact]
    public void ReadScript_NeverDropsAnything()
    {
        // Arrange — the vendored script must never be able to delete data, however it's
        // invoked.
        // Act
        var script = QuartzSchemaProvisioner.ReadScript();

        // Assert
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
    }
}
