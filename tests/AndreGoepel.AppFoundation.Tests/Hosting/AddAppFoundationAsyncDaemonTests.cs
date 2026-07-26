using AndreGoepel.AppFoundation.Hosting;
using JasperFx.Events.Daemon;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public sealed class AddAppFoundationAsyncDaemonTests
{
    [Fact]
    public void AddAppFoundation_DefaultOptions_LeavesAsyncDaemonDisabled()
    {
        // Arrange — EnableAsyncDaemon defaults to false, so existing consumers see no
        // behavior change until they opt in (#153).
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        Assert.Equal(DaemonMode.Disabled, ResolveAsyncMode(builder));
    }

    [Fact]
    public void AddAppFoundation_EnableAsyncDaemon_DefaultsToSoloMode()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation(options => options.EnableAsyncDaemon = true);

        // Assert
        Assert.Equal(DaemonMode.Solo, ResolveAsyncMode(builder));
    }

    [Fact]
    public void AddAppFoundation_EnableAsyncDaemonWithExplicitMode_UsesThatMode()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation(options =>
        {
            options.EnableAsyncDaemon = true;
            options.AsyncDaemonMode = DaemonMode.HotCold;
        });

        // Assert
        Assert.Equal(DaemonMode.HotCold, ResolveAsyncMode(builder));
    }

    [Fact]
    public void AddAppFoundation_AsyncDaemonModeWithoutEnabling_HasNoEffect()
    {
        // Arrange — setting the mode alone must not turn the daemon on; EnableAsyncDaemon
        // is the single on/off switch.
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation(options => options.AsyncDaemonMode = DaemonMode.HotCold);

        // Assert
        Assert.Equal(DaemonMode.Disabled, ResolveAsyncMode(builder));
    }

    private static DaemonMode ResolveAsyncMode(WebApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        return ((StoreOptions)store.Options).Projections.AsyncMode;
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
