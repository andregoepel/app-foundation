using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class AddAppFoundationQuartzTests
{
    [Fact]
    public void AddAppFoundation_ConfiguresPersistentPostgresJobStore()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        var quartz = ResolveQuartzOptions(builder);
        Assert.Equal("Quartz.Impl.AdoJobStore.JobStoreTX, Quartz", quartz["quartz.jobStore.type"]);
        Assert.Equal(
            "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz",
            quartz["quartz.jobStore.driverDelegateType"]
        );
        Assert.Equal("Npgsql", quartz["quartz.dataSource.default.provider"]);
    }

    [Fact]
    public void AddAppFoundation_UsesLowercaseTablePrefix()
    {
        // Arrange — Postgres folds unquoted identifiers to lowercase; the vendored schema
        // creates lowercase qrtz_* tables, so Quartz's own default ("QRTZ_") must be
        // overridden (#129).
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        Assert.Equal("qrtz_", ResolveQuartzOptions(builder)["quartz.jobStore.tablePrefix"]);
    }

    [Fact]
    public void AddAppFoundation_UsesSystemTextJsonSerializer()
    {
        // Arrange — Quartz's default (BinaryObjectSerializer) uses BinaryFormatter, which
        // throws on .NET 8+.
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        Assert.Equal(
            "Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson",
            ResolveQuartzOptions(builder)["quartz.serializer.type"]
        );
    }

    [Fact]
    public void AddAppFoundation_DoesNotConnectToTheDatabase()
    {
        // Arrange — AddAppFoundation must stay side-effect-free against the connection
        // string (mirrors AddMarten): resolving config alone must not attempt a real
        // connection, even though the configured connection string here is unreachable.
        var builder = CreateBuilder();

        // Act / Assert — no exception
        builder.AddAppFoundation();
        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzOptions>>();
    }

    private static QuartzOptions ResolveQuartzOptions(WebApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:appfoundation-database"] =
                    "Host=localhost;Port=5432;Database=test;Username=u;Password=p",
            }
        );
        return builder;
    }
}
