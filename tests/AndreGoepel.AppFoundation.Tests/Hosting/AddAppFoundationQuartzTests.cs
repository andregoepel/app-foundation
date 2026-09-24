using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Extensibility;
using Quartz.Impl;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public sealed class AddAppFoundationQuartzTests
{
    [Fact]
    public void AddAppFoundation_ConfiguresPersistentPostgresJobStore()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        using var provider = builder.Services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;
        var dataSource = provider
            .GetRequiredService<IOptionsMonitor<DataSourceOptions>>()
            .Get(store.DataSource);

        Assert.True(provider.GetRequiredService<IJobStore>().SupportsPersistence);
        Assert.Equal("quartz", store.DataSource);
        Assert.Equal(DataSourceOptions.Providers.Npgsql, dataSource.Provider);
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
        using var provider = builder.Services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;
        Assert.Equal("qrtz_", store.TablePrefix);
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
        using var provider = builder.Services.BuildServiceProvider();
        Assert.IsType<SystemTextJsonObjectSerializer>(
            provider.GetRequiredService<IObjectSerializer>()
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
        provider.GetRequiredService<IOptions<AdoJobStoreOptions>>();
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
